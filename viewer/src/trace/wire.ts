/*
 * The v2 wire, exactly as src/ProtoTest.Core/Tracing/ProtoTraceWire.cs writes it. That serializer is the
 * authority: when a field here disagrees with it, this file is wrong. Enum values arrive as the C# names
 * (sections, tones) or lowercased (phase, status, outcome, source); the model normalizes both.
 */

export type WireAttributes = Record<string, string | null>;

export interface WireError {
  type: string;
  message: string;
}

export interface WireSectionItem {
  label: string;
  value: string | null;
  detail: string | null;
  tone: string;
}

export interface WireSection {
  label: string;
  kind: string;
  items: WireSectionItem[] | null;
  content: string | null;
  language: string | null;
}

export interface WireTimelineEvent {
  record?: undefined;
  name: string;
  atUtc: string;
  kind: string;
  source: string;
  outcome: string;
  error: WireError | null;
  attributes: WireAttributes | null;
  sections: WireSection[] | null;
}

export interface WireObservation {
  record: "observation";
  name: string;
  atUtc: string;
  kind: string;
  identifier: string | null;
  data: string | null;
  metadata: WireAttributes | null;
}

export interface WireAttachment {
  record: "attachment";
  name: string;
  atUtc: string;
  artifactId: string | null;
}

export interface WireFinding {
  record: "finding";
  name: string;
  atUtc: string;
  status: string;
  category: string | null;
  targetName: string | null;
  tags: string[] | null;
  metadata: WireAttributes | null;
}

export type WireRecordEvent = WireObservation | WireAttachment | WireFinding;
export type WireEvent = WireTimelineEvent | WireRecordEvent;

export interface WireSpan {
  spanId: string;
  parentSpanId: string | null;
  name: string;
  kind: string;
  source: string;
  phase: string;
  status: string;
  error: WireError | null;
  startedAtUtc: string;
  durationMs: number | null;
  count: number | null;
  entityKind: string | null;
  entityId: string | null;
  attributes: WireAttributes | null;
  sections: WireSection[] | null;
  events: WireEvent[];
}

export interface WireArtifact {
  id: string;
  name: string;
  mediaType: string;
  description: string | null;
  archivePath: string;
  sizeBytes: number | null;
  error: string | null;
}

export interface WireTestAttributes {
  testId: string;
  testName: string;
  testClass: string | null;
  testMethod: string;
  testOutcome: string;
  testDurationMs: number;
}

/** The run's resource: identity, timing and the environment as flat `environment.*` attributes. */
export interface WireRunAttributes {
  runId: string;
  runStartedAtUtc: string;
  runCompletedAtUtc: string | null;
  [environment: `environment.${string}`]: string | null | undefined;
}

export interface WireResourceGroup {
  resource: { attributes: WireTestAttributes | WireRunAttributes };
  artifacts: WireArtifact[];
  scopeSpans: {
    scope: { name: string; version: string };
    spans: WireSpan[];
    /** Events with no operation above them: a run's gate verdicts, record items outside any span. */
    events: WireEvent[] | null;
  }[];
}

export interface WireSpans {
  formatVersion: string;
  resourceSpans: WireResourceGroup[];
}

export interface WireChange {
  atUtc: string;
  operationId: string | null;
  change: string;
  state: WireAttributes | null;
  source: string;
  inferred: boolean;
}

export interface WireItem {
  kind: string;
  id: string;
  name: string;
  scope: string | null;
  firstSeenUtc: string;
  lastSeenUtc: string;
  state: WireAttributes;
  changes: WireChange[];
}

export interface WireState {
  formatVersion: string;
  run: { items: WireItem[] };
  tests: { testId: string; name: string; items: WireItem[] }[];
}

export interface WireManifest {
  formatVersion: string;
  runEntry?: string;
  spansEntry?: string;
  stateEntry?: string;
}
