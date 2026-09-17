export type TraceOutcome = "Unknown" | "Succeeded" | "Partial" | "Failed" | "Cancelled" | "Skipped";
export type TracePhase = "Run" | "Setup" | "Execution" | "Rollback" | "Teardown";
export type TraceEntryKind = "Operation" | "Event";

export interface TraceError {
  type: string;
  message: string;
  stackTrace?: string | null;
}

export interface TraceEntry {
  id: string;
  parentId?: string | null;
  entryKind: TraceEntryKind;
  kind: string;
  name: string;
  source: string;
  phase: TracePhase;
  timestampUtc: string;
  duration?: string | number | null;
  outcome: TraceOutcome;
  attributes: Record<string, string | null>;
  error?: TraceError | null;
}

export interface TraceArtifact {
  id: string;
  name: string;
  mediaType: string;
  description?: string | null;
  archivePath: string;
  error?: string | null;
}

export interface TestTrace {
  testId: string;
  name: string;
  className?: string | null;
  methodName: string;
  startedAtUtc: string;
  duration: string | number;
  outcome: TraceOutcome;
  error?: TraceError | null;
  entries: TraceEntry[];
  artifacts?: TraceArtifact[];
}

export interface TraceRun {
  formatVersion: string;
  runId: string;
  startedAtUtc: string;
  completedAtUtc?: string | null;
  tests: TestTrace[];
  environment?: Record<string, string>;
  artifacts?: TraceArtifact[];
  /** Operations that belong to the run rather than to one test: owned resources, gate verdicts. */
  entries?: TraceEntry[] | null;
}

export interface TraceTreeItem {
  entry: TraceEntry;
  children: TraceTreeItem[];
}

export interface ShapeMismatch {
  propertyPath?: string;
  path?: string;
  reason: string;
  expected: unknown;
  actual: unknown;
}

export type ShapeCheckStatus = "matched" | "failed" | "branch";

export interface ShapeCheckNode {
  key: string;
  path: string;
  status: ShapeCheckStatus;
  mismatch?: ShapeMismatch;
  expected?: unknown;
  actual?: unknown;
  children: ShapeCheckNode[];
}

export const phaseOrder: TracePhase[] = ["Setup", "Execution", "Rollback", "Teardown", "Run"];
