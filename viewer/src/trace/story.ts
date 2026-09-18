import type { Phase, Span, TestTrace } from "./model";
import { phases } from "./model";
import { isCheck } from "./format";

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

export type StoryRow = StoryStep | StoryGroup;

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

/** The phases of a test; each phase's lifecycle span (test.setup, ...) is the heading, its children the rows. */
export function story(test: TestTrace): StoryPhase[] {
  return phases
    .map(phase => {
      const roots = test.roots.filter(span => span.phase === phase);
      const lifecycle = roots.find(span => span.kind === `test.${phase}`) ?? null;
      const spans = roots.flatMap(span => span === lifecycle ? span.children : [span]);
      return { phase, span: lifecycle, rows: rows(spans) };
    })
    .filter(phase => phase.span || phase.rows.length);
}

/** Every step and group id on the way to a span, so a selection made elsewhere can be unfolded. */
export function pathTo(phasesToSearch: StoryPhase[], spanId: string): string[] {
  const search = (list: StoryRow[], trail: string[]): string[] | null => {
    for (const row of list) {
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
