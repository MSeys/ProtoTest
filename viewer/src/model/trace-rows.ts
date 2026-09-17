import type { TestTrace, TraceEntry, TraceOutcome, TracePhase, TraceTreeItem } from "./trace-schema";
import { phaseOrder } from "./trace-schema";
import { entryLevel, isEvidenceEntry } from "./trace-levels";
import { milliseconds, shortenType } from "./trace-format";

/** Rebuilds the parent/child tree from the flat entries. */
export function buildTree(entries: TraceEntry[]): TraceTreeItem[] {
  const items = new Map<string, TraceTreeItem>(
    entries.map(entry => [entry.id, { entry, children: [] }] as [string, TraceTreeItem]));
  const roots: TraceTreeItem[] = [];
  for (const item of items.values()) {
    const parent = item.entry.parentId ? items.get(item.entry.parentId) : undefined;
    if (parent) parent.children.push(item); else roots.push(item);
  }
  return roots;
}

export interface StoryStep {
  entry: TraceEntry;
  /** Nested steps and the evidence they produced, in the order they happened. */
  items: StoryItem[];
  /** The resolution and plumbing behind the step, folded away until a reader asks. */
  machinery: TraceEntry[];
}

export type StoryItem =
  | { type: "step"; step: StoryStep }
  | { type: "evidence"; entry: TraceEntry };

export interface StoryPhase {
  phase: TracePhase;
  summary: ReturnType<typeof phaseSummary>;
  items: StoryItem[];
  machinery: TraceEntry[];
}

/**
 * The story of a test: per phase, the operations that actually happened in the order they happened, each
 * carrying the evidence it produced and the machinery it took. Nothing is dropped — plumbing is folded onto
 * the step that owns it and is always one click away — and a failure is never folded, at any depth.
 */
export function storyRows(test: TestTrace): StoryPhase[] {
  return phaseOrder
    .map(phase => {
      const entries = test.entries.filter(entry => entry.phase === phase);
      return { phase, summary: phaseSummary(test, phase, entries), ...storySteps(entries) };
    })
    .filter(item => item.summary.entries.length);
}

export function countStoryItems(items: StoryItem[]): number {
  return items.reduce((sum, item) => sum + 1 + (item.type === "step" ? countStoryItems(item.step.items) : 0), 0);
}

function storySteps(entries: TraceEntry[]): { items: StoryItem[]; machinery: TraceEntry[] } {
  const items: StoryItem[] = [];
  const machinery: TraceEntry[] = [];
  const visit = (node: TraceTreeItem, step: StoryStep | undefined, depth: number) => {
    const entry = node.entry;
    if (isEvidenceEntry(entry)) {
      (step ? step.items : items).push({ type: "evidence", entry });
      node.children.forEach(child => visit(child, step, depth));
      return;
    }
    if (entryLevel(entry) === "normal") {
      const next: StoryStep = { entry, items: [], machinery: [] };
      if (step && depth < 3) step.items.push({ type: "step", step: next });
      else items.push({ type: "step", step: next });
      node.children.forEach(child => visit(child, next, depth + 1));
      return;
    }
    (step ? step.machinery : machinery).push(entry);
    node.children.forEach(child => visit(child, step, depth));
  };
  for (const root of buildTree(entries)) visit(root, undefined, 0);
  return { items, machinery };
}

export interface OwnershipLifeline {
  id: string;
  kind: string;
  label: string;
  /** Where the test claimed it: client.initialize, client.register or resource.register. */
  from?: TraceEntry;
  /** Where the test released it: resource.release. */
  to?: TraceEntry;
  /** How many operations resolved it while it was alive. */
  usedBy: number;
}

/** What a test owned, from the moment it claimed it to the moment it released it. */
export function ownershipLifelines(test: TestTrace): OwnershipLifeline[] {
  const lifelines = new Map<string, OwnershipLifeline>();
  const ensure = (id: string, kind: string, label: string) => {
    let line = lifelines.get(id);
    if (!line) { line = { id, kind, label, usedBy: 0 }; lifelines.set(id, line); }
    return line;
  };
  const clientId = (entry: TraceEntry) => `client:${entry.attributes?.["client.type"] ?? ""}:${entry.attributes?.["client.name"] ?? ""}`;
  for (const entry of test.entries) {
    const attributes = entry.attributes ?? {};
    if (entry.kind === "client.initialize" || entry.kind === "client.register") {
      const line = ensure(clientId(entry), "client", `Client ${shortenType(attributes["client.type"]) ?? ""} '${attributes["client.name"] ?? ""}'`);
      if (entry.kind === "client.initialize" || !line.from) line.from = entry;
    } else if (entry.kind === "client.resolve" || entry.kind === "client.try_resolve") {
      const line = lifelines.get(clientId(entry));
      if (line) line.usedBy++;
    } else if (entry.kind === "resource.register") {
      const id = attributes["resource.id"] ?? entry.id;
      ensure(id, attributes["resource.kind"] ?? "resource", attributes["resource.description"] ?? id).from = entry;
    } else if (entry.kind === "resource.release") {
      const id = attributes["resource.id"] ?? entry.id;
      const line = ensure(id, attributes["resource.kind"] ?? "resource", attributes["resource.description"] ?? id);
      line.to = entry;
      if (attributes["resource.description"]) line.label = attributes["resource.description"];
    }
  }
  return [...lifelines.values()];
}

/** The failure a reader most likely opened the test for: deepest, with an error, an assertion or a shape mismatch. */
export function primaryFailure(test: TestTrace): TraceEntry | undefined {
  const entries = new Map(test.entries.map(entry => [entry.id, entry]));
  const depth = (entry: TraceEntry) => {
    let result = 0;
    let current: TraceEntry | undefined = entry;
    const visited = new Set<string>();
    while (current?.parentId && !visited.has(current.id)) {
      visited.add(current.id);
      current = entries.get(current.parentId);
      if (current) result++;
    }
    return result;
  };
  return test.entries
    .filter(entry => entry.outcome === "Failed" || entry.error)
    .map(entry => ({
      entry,
      score: depth(entry) * 10
        + (entry.error ? 100 : 0)
        + (entry.kind.startsWith("assert.") || entry.attributes?.["shape.result"] === "mismatched" ? 1000 : 0)
        + (entry.kind.startsWith("test.") ? 0 : 100)
    }))
    .sort((left, right) => right.score - left.score)[0]?.entry;
}

export function phaseSummary(test: TestTrace, phase: TracePhase, phaseEntries?: TraceEntry[]) {
  const entries = phaseEntries ?? test.entries.filter(entry => entry.phase === phase);
  const lifecycle = entries.find(entry => entry.kind === `test.${phase.toLocaleLowerCase()}`);
  const failed = entries.find(entry => entry.outcome === "Failed");
  const partial = lifecycle?.outcome === "Partial" || (test.outcome === "Partial" && Boolean(failed));
  const succeeded = entries.some(entry => entry.outcome === "Succeeded");
  const timestamps = entries.map(entry => Date.parse(entry.timestampUtc));
  const ends = entries.map(entry => Date.parse(entry.timestampUtc) + milliseconds(entry.duration));
  return {
    phase,
    entries,
    operations: entries.filter(entry => entry.entryKind === "Operation").length,
    events: entries.filter(entry => entry.entryKind === "Event").length,
    outcome: partial ? "Partial" as TraceOutcome : failed ? "Failed" as TraceOutcome : lifecycle?.outcome ?? (succeeded ? "Succeeded" as TraceOutcome : "Unknown" as TraceOutcome),
    duration: lifecycle?.duration ? milliseconds(lifecycle.duration) : timestamps.length ? Math.max(...ends) - Math.min(...timestamps) : 0
  };
}

export function runDuration(tests: TestTrace[]): number {
  if (!tests.length) return 0;
  const start = Math.min(...tests.map(test => Date.parse(test.startedAtUtc)));
  const end = Math.max(...tests.map(test => Date.parse(test.startedAtUtc) + milliseconds(test.duration)));
  return end - start;
}

/** Where a phase sits inside its test: the story view's x axis. */
export function testSegments(test: TestTrace) {
  const started = Date.parse(test.startedAtUtc);
  const total = Math.max(milliseconds(test.duration), 1);
  const segments: { phase: TracePhase; outcome: TraceOutcome; left: number; width: number }[] = [];
  for (const phase of phaseOrder) {
    if (phase === "Run") continue;
    const entries = test.entries.filter(entry => entry.phase === phase);
    if (!entries.length) continue;
    const from = Math.min(...entries.map(entry => Date.parse(entry.timestampUtc)));
    const to = Math.max(...entries.map(entry => Date.parse(entry.timestampUtc) + milliseconds(entry.duration)));
    segments.push({
      phase,
      outcome: phaseSummary(test, phase).outcome,
      left: Math.max(0, ((from - started) / total) * 100),
      width: Math.max(.5, ((to - from) / total) * 100)
    });
  }
  return segments;
}
