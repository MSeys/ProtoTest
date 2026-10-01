import type { WireAttributes, WireError } from "./wire";

/*
 * The viewer's model: the two wire documents, normalized and cross-linked. Nothing here is a new model of
 * the trace - every object is a span, an event, an artifact or a tracked item from the wire - but each one
 * knows its neighbours, so a screen never has to search: a span knows the state it changed, a change knows
 * the span that caused it, a test knows the failure a reader opened it for.
 */

export type Phase = "run" | "setup" | "execution" | "rollback" | "teardown";
export type Outcome = "succeeded" | "partial" | "failed" | "cancelled" | "skipped" | "unknown";
export type Tone = "neutral" | "success" | "warning" | "error";
/** How a value reached the trace: the test said so, the test saw it, or the application reported it. */
export type ChangeSource = "testside" | "observed" | "applicationside";

export const phases: Phase[] = ["setup", "execution", "rollback", "teardown"];

export interface SectionItem {
  label: string;
  value: string | null;
  detail: string | null;
  tone: Tone;
}

/** A section's kind is open: fields, code, checks and diff have renderers; anything else renders generically. */
export interface Section {
  label: string;
  kind: string;
  items: SectionItem[];
  content: string | null;
  language: string | null;
}

export interface Artifact {
  id: string;
  name: string;
  mediaType: string;
  description: string | null;
  archivePath: string;
  sizeBytes: number | null;
  error: string | null;
}

/** Something that happened at a moment inside an operation: a server started, a subscription got a message. */
export interface Moment {
  at: number;
  name: string;
  kind: string;
  source: string;
  outcome: Outcome;
  error: WireError | null;
  attributes: WireAttributes;
  sections: Section[];
  span: Span | null;
}

/** What the test learned and kept: an observation, an attached file, a finding. */
export type Evidence =
  | { type: "observation"; at: number; target: string; kind: string; identifier: string | null; data: string | null; metadata: WireAttributes; span: Span | null }
  | { type: "attachment"; at: number; name: string; artifact: Artifact | null; span: Span | null }
  | { type: "finding"; at: number; message: string; status: string; category: string | null; target: string | null; tags: string[]; metadata: WireAttributes; span: Span | null };

export interface Change {
  at: number;
  change: string;
  state: WireAttributes;
  source: ChangeSource;
  inferred: boolean;
  item: Item;
  /** The operation that caused it, when the trace knows. */
  span: Span | null;
}

/** One thing that existed: a client, a context, a database, a capability, a domain object the app reported. */
export interface Item {
  key: string;
  kind: string;
  id: string;
  name: string;
  scope: string | null;
  firstSeen: number;
  lastSeen: number;
  state: WireAttributes;
  changes: Change[];
  /** The test it belongs to, or null for the run's own items. */
  test: TestTrace | null;
}

export interface Span {
  id: string;
  parent: Span | null;
  children: Span[];
  depth: number;
  name: string;
  kind: string;
  source: string;
  phase: Phase;
  status: Outcome;
  error: WireError | null;
  start: number;
  duration: number;
  end: number;
  /** How many times a repeated operation ran; 1 for the ordinary case. */
  count: number;
  attributes: WireAttributes;
  sections: Section[];
  moments: Moment[];
  evidence: Evidence[];
  /** The tracked item this operation acted on, as a state key, and the item itself once linked. */
  itemKey: string | null;
  item: Item | null;
  /** What this operation changed in the state map. */
  changes: Change[];
  test: TestTrace | null;
}

export interface ShapeMismatch {
  path: string;
  reason: string;
  expected: unknown;
  actual: unknown;
}

/** The answer a reader opened a failing test for: what failed, how, and what led to it. */
export interface Failure {
  span: Span;
  /** The failing check, when the span recorded one. */
  check: SectionItem | null;
  mismatches: ShapeMismatch[];
  /** The call whose response the check judged, when there is one. */
  call: Span | null;
}

export interface TestTrace {
  /** 1-based, in start order: the number that ties the run list, the rail and the timeline together. */
  number: number;
  id: string;
  name: string;
  className: string | null;
  method: string;
  outcome: Outcome;
  duration: number;
  start: number;
  end: number;
  spans: Span[];
  roots: Span[];
  byId: Map<string, Span>;
  /** Events and evidence with no operation above them. */
  moments: Moment[];
  evidence: Evidence[];
  artifacts: Map<string, Artifact>;
  items: Item[];
  failure: Failure | null;
}

export interface Gate {
  name: string;
  status: string;
  message: string | null;
  details: string | null;
  outcome: Outcome;
  at: number;
}

/** What the run could see, derived from the state it recorded, so a gap is stated rather than implied. */
export interface Visibility {
  hosting: "in-process" | "remote" | "unknown";
  capabilities: Item[];
  backends: string[];
  sources: ChangeSource[];
  applicationInstrumented: boolean;
}

export interface Run {
  id: string;
  start: number;
  end: number;
  duration: number;
  environment: Record<string, string>;
  tests: TestTrace[];
  spans: Span[];
  moments: Moment[];
  evidence: Evidence[];
  artifacts: Map<string, Artifact>;
  items: Item[];
  gates: Gate[];
  findings: { finding: Extract<Evidence, { type: "finding" }>; test: TestTrace | null }[];
  visibility: Visibility;
  counts: Record<Outcome, number>;
}

// Re-exported so a view keeps importing the model from one place; the implementation lives in ./build
// (wire to model) and ./analysis (the facts derived from it).
export { buildRun } from "./build";
export { checkItems, deriveVisibility, findFailure, shapeMismatches } from "./analysis";

