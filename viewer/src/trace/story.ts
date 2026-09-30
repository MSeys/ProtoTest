import type { Phase, Span, TestTrace } from "./model";
import { phases } from "./model";
import { isCheck } from "./format";
import { untracedGaps, type UntracedGap } from "./analysis";

/*
 * The story of a test, built on the span tree without changing it: per phase, the operations in the order
 * they ran. A call carries the checks that judged it on its own row. The framework's own steps - extensions
 * that set a test up, clients it initialized, resources it released - fold into one row per run of them when
 * nothing but machinery happened inside. An extension that made a call or built data stays a step, and a
 * failure is never folded.
 */

export interface StoryStep {
  type: "step";
  span: Span;
  /** The checks that judged this operation, shown on its row rather than as rows of their own. */
  checks: Span[];
  children: StoryRow[];
}

export interface StoryGroup {
  type: "group";
  /** Stable across renders: the first span's id. */
  id: string;
  spans: Span[];
  /** "7 extensions" when they all are, "4 framework steps" otherwise. */
  label: string;
  rows: StoryRow[];
}

/** Time in the phase with no operation recorded, in the place it happened. */
export interface StoryGap {
  type: "gap";
  id: string;
  gap: UntracedGap;
}

export type StoryRow = StoryStep | StoryGroup | StoryGap;

export interface StoryPhase {
  phase: Phase;
  span: Span | null;
  rows: StoryRow[];
}

const framework = /^(hook|attribute|client|context|resources?|attachment)\./;

export function isFramework(span: Span): boolean {
  return framework.test(span.kind);
}

/** Only machinery all the way down: nothing a reader would call part of the scenario happened inside. */
function pureFramework(span: Span): boolean {
  return isFramework(span) && span.children.every(child => isFramework(child) && pureFramework(child));
}

/** The rows with the framework left out: a step stays when scenario work or a failure happened inside it. */
export function withoutFramework(list: StoryRow[]): StoryRow[] {
  return list.flatMap((row): StoryRow[] => {
    if (row.type === "group") return [];
    if (row.type !== "step") return [row];
    if (pureFramework(row.span) && !fails(row.span)) return [];
    return [{ ...row, children: withoutFramework(row.children) }];
  });
}

export function fails(span: Span): boolean {
  return span.status === "failed" || span.status === "cancelled" || Boolean(span.error) || span.children.some(fails);
}

function step(span: Span): StoryStep {
  return {
    type: "step",
    span,
    checks: span.children.filter(isCheck),
    children: rows(span.children.filter(child => !isCheck(child)))
  };
}

function rows(spans: Span[]): StoryRow[] {
  const result: StoryRow[] = [];
  let run: Span[] = [];
  const flush = () => {
    if (run.length > 1) {
      const extensions = run.every(span => /^(hook|attribute)\./.test(span.kind));
      result.push({
        type: "group",
        id: run[0].id,
        spans: run,
        label: extensions ? `${run.length} extensions` : `${run.length} framework steps`,
        rows: run.map(step)
      });
    } else {
      run.forEach(span => result.push(step(span)));
    }
    run = [];
  };
  for (const span of spans) {
    if (pureFramework(span) && !fails(span)) { run.push(span); continue; }
    flush();
    result.push(step(span));
  }
  flush();
  return result;
}

function rowStart(row: StoryRow): number {
  if (row.type === "gap") return row.gap.start;
  return row.type === "group" ? Math.min(...row.spans.map(span => span.start)) : row.span.start;
}

function holds(row: StoryRow, span: Span): boolean {
  return row.type === "group" ? row.spans.includes(span) : row.type === "step" && row.span === span;
}

/** Each gap goes right before the operation that ended it, or last when the phase ended with it. */
function withGaps(list: StoryRow[], gaps: UntracedGap[]): StoryRow[] {
  if (!gaps.length) return list;
  const pending = [...gaps];
  const result: StoryRow[] = [];
  for (const row of list) {
    while (pending.length && pending[0].before && (holds(row, pending[0].before) || rowStart(row) > pending[0].before.start)) {
      const gap = pending.shift()!;
      result.push({ type: "gap", id: `gap-${gap.phase}-${gap.start}`, gap });
    }
    result.push(row);
  }
  for (const gap of pending) result.push({ type: "gap", id: `gap-${gap.phase}-${gap.start}`, gap });
  return result;
}

/** The phases of a test; each phase's lifecycle span (test.setup, ...) is the heading, its children the rows. */
export function story(test: TestTrace): StoryPhase[] {
  const gaps = untracedGaps(test);
  return phases
    .map(phase => {
      const roots = test.roots.filter(span => span.phase === phase);
      const lifecycle = roots.find(span => span.kind === `test.${phase}`) ?? null;
      const spans = roots.flatMap(span => span === lifecycle ? span.children : [span]);
      return { phase, span: lifecycle, rows: withGaps(rows(spans), gaps.filter(gap => gap.lifecycle === lifecycle)) };
    })
    .filter(phase => phase.span || phase.rows.length);
}

/** Every step and group id on the way to a span, so a selection made elsewhere can be unfolded. */
export function pathTo(phasesToSearch: StoryPhase[], spanId: string): string[] {
  const search = (list: StoryRow[], trail: string[]): string[] | null => {
    for (const row of list) {
      if (row.type === "gap") continue;
      const id = row.type === "group" ? row.id : row.span.id;
      if (row.type === "step" && (row.span.id === spanId || row.checks.some(check => check.id === spanId))) return [...trail, id];
      const inner = search(row.type === "group" ? row.rows : row.children, [...trail, id]);
      if (inner) return inner;
    }
    return null;
  };
  for (const phase of phasesToSearch) {
    const found = search(phase.rows, []);
    if (found) return found;
  }
  return [];
}
