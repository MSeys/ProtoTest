import type { ChangeSource, Evidence, Failure, Item, ShapeMismatch, Span, TestTrace, Visibility } from "./model";

/** Kinds whose span a failure belongs to, the same set the diagnosis names as call ancestors. */
const callKinds = new Set(["http.request", "graphql.operation", "grpc.call"]);

function isCall(span: Span) {
  return callKinds.has(span.kind) || span.kind.startsWith("messaging.");
}

/** The verdict items of a span's Checks sections, in order. */
export function checkItems(span: Span) {
  return span.sections.flatMap(section => section.kind === "checks" ? section.items : []);
}

/**
 * The deepest failing span, preferring a check, then anything with an error, over the phase that
 * contains it. A failed span outranks a cancelled one whatever the depth, so a cancelled child that
 * recorded an error never hides the failure above it.
 */
export function findFailure(test: TestTrace): Failure | null {
  const failing = test.spans.filter(span => span.status === "failed" || span.error);
  if (!failing.length) return null;
  const score = (span: Span) =>
    span.depth * 10 + (span.error ? 100 : 0) + (span.kind.startsWith("assert.") ? 1000 : 0) + (span.kind.startsWith("test.") ? -500 : 0);
  const span = failing.reduce((best, candidate) => {
    const bestTier = best.status === "failed" ? 1 : 0;
    const candidateTier = candidate.status === "failed" ? 1 : 0;
    return candidateTier > bestTier || (candidateTier === bestTier && score(candidate) > score(best)) ? candidate : best;
  });
  const check = checkItems(span).find(item => item.tone === "error") ?? null;
  let call: Span | null = span;
  while (call && !isCall(call)) call = call.parent;
  return { span, check, mismatches: shapeMismatches(span), call };
}

/** A shape check's mismatches, from its recorded list or, for a third-party check, from a diff section. */
export function shapeMismatches(span: Span): ShapeMismatch[] {
  const recorded = span.attributes["shape.mismatches"];
  if (recorded) {
    try {
      const parsed = JSON.parse(recorded) as { propertyPath?: string; reason?: string; expected?: unknown; actual?: unknown }[];
      return parsed.map(entry => ({ path: entry.propertyPath ?? "", reason: entry.reason ?? "", expected: entry.expected, actual: entry.actual }));
    } catch { /* fall through to sections */ }
  }
  return span.sections
    .filter(section => section.kind === "diff")
    .flatMap(section => section.items.map(item => ({ path: item.label, reason: "", expected: item.value, actual: item.detail })));
}

/** Time inside a phase with no operation recorded: a wait, or work the trace could not see. */
export interface UntracedGap {
  phase: Span["phase"];
  /** The phase's own span (test.setup, test.execution, ...). */
  lifecycle: Span;
  start: number;
  duration: number;
  /** The operation that ended the gap; null when the phase ended with it. */
  before: Span | null;
}

/**
 * The gaps between a phase's operations that are worth stating: from 15% of the phase, at least 20 ms,
 * and always from 250 ms. Smaller gaps are the ordinary cost of the framework between two steps.
 */
export function untracedGaps(test: TestTrace): UntracedGap[] {
  const gaps: UntracedGap[] = [];
  for (const lifecycle of test.roots.filter(span => span.kind === `test.${span.phase}`)) {
    const threshold = Math.min(250, Math.max(20, lifecycle.duration * 0.15));
    const children = [...lifecycle.children].sort((left, right) => left.start - right.start);
    let cursor = lifecycle.start;
    for (const child of children) {
      if (child.start - cursor >= threshold) {
        gaps.push({ phase: lifecycle.phase, lifecycle, start: cursor, duration: child.start - cursor, before: child });
      }
      cursor = Math.max(cursor, child.end);
    }
    if (lifecycle.end - cursor >= threshold) {
      gaps.push({ phase: lifecycle.phase, lifecycle, start: cursor, duration: lifecycle.end - cursor, before: null });
    }
  }
  return gaps;
}

/** The findings a test recorded, on its operations or with none above them. */
export function testFindings(test: TestTrace): Extract<Evidence, { type: "finding" }>[] {
  return [...test.spans.flatMap(span => span.evidence), ...test.evidence]
    .filter((entry): entry is Extract<Evidence, { type: "finding" }> => entry.type === "finding");
}

export type DiagnosisRule = "assertion" | "operation-error" | "runner-failure" | "finding";

export const diagnosisRuleLabels: Record<DiagnosisRule, string> = {
  assertion: "Assertion",
  "operation-error": "Operation error",
  "runner-failure": "Runner failure",
  finding: "Finding"
};

/**
 * Why a test did not pass, named with the rules `prototest summary` and the MCP diagnosis use, in their
 * order: a failed check, a failed operation, a failure the runner reported, then a finding.
 */
export function diagnosisRule(test: TestTrace): DiagnosisRule | null {
  const failure = test.failure;
  if (failure) {
    if (failure.span.status === "failed" && failure.span.kind.startsWith("assert.") && (failure.mismatches.length || failure.check)) return "assertion";
    if (failure.span.error) return failure.span.kind.startsWith("test.") ? "runner-failure" : "operation-error";
  }
  return testFindings(test).length ? "finding" : null;
}

/** One capability as a reader names it: the same clock composed for three hosts reads once, with its instances. */
export interface CapabilityGroup {
  name: string;
  count: number;
  instances: string[];
  sources: string[];
}

/** Groups the run's capabilities by name, in the order the run composed them. */
export function groupCapabilities(capabilities: Item[]): CapabilityGroup[] {
  const groups = new Map<string, CapabilityGroup>();
  const add = (list: string[], value: unknown) => {
    if (typeof value === "string" && value && !list.includes(value)) list.push(value);
  };
  for (const item of capabilities) {
    const group = groups.get(item.name) ?? { name: item.name, count: 0, instances: [], sources: [] };
    group.count++;
    add(group.instances, item.state["capability.instance"]);
    add(group.sources, item.state["capability.source"]);
    groups.set(item.name, group);
  }
  return [...groups.values()].map(group => ({ ...group, instances: [...group.instances].sort((left, right) => left.localeCompare(right)) }));
}

/** The run's resources of one kind, as short names under one label. */
export interface ResourceGroup {
  label: string;
  entries: { text: string; title: string }[];
}

// Kinds read in this order; a fake recorded as a server and as a WireMock resource is one fake.
const resourceKinds: [string[], string][] = [
  [["application"], "Applications"], [["aspire"], "Aspire"], [["database"], "Databases"], [["broker"], "Messaging"],
  [["server", "wiremock"], "Fakes"], [["worker"], "Workers"], [["readiness"], "Readiness"], [["setup"], "Setup"]
];

/** Groups the run's resources by kind, with the kind prefix taken off each name and repeats merged. */
export function groupResources(items: Item[]): ResourceGroup[] {
  const groups = new Map<string, ResourceGroup>();
  const labelOf = (kind: string) =>
    resourceKinds.find(([kinds]) => kinds.includes(kind))?.[1] ?? kind.charAt(0).toUpperCase() + kind.slice(1);
  for (const item of items) {
    const description = item.state["resource.description"];
    const full = typeof description === "string" && description ? description : item.name;
    // "Readiness · Csms address" reads "Csms address" under Readiness; "WireMock fake 'X'" reads "X" under Fakes.
    const quoted = /'([^']+)'$/.exec(full);
    const text = quoted ? quoted[1] : full.includes(" · ") ? full.slice(full.indexOf(" · ") + 3) : full;
    const state = item.state["resource.state"] ?? item.state["server.state"];
    const label = labelOf(item.kind);
    const group = groups.get(label) ?? { label, entries: [] };
    if (!group.entries.some(entry => entry.text === text)) {
      group.entries.push({ text, title: typeof state === "string" && state ? `${item.id} (${state})` : item.id });
    }
    groups.set(label, group);
  }
  const order = (label: string) => {
    const index = resourceKinds.findIndex(([, name]) => name === label);
    return index < 0 ? resourceKinds.length : index;
  };
  return [...groups.values()].sort((left, right) => order(left.label) - order(right.label) || left.label.localeCompare(right.label));
}

/** What a run could see, derived from the state it recorded. */
export function deriveVisibility(runItems: Item[], tests: TestTrace[]): Visibility {
  const capabilities = runItems.filter(item => item.kind === "capability");
  const capabilityKind = (item: Item) => item.state["capability.kind"] ?? null;
  const testItems = tests.flatMap(test => test.items);
  const hasServer = capabilities.some(item => capabilityKind(item) === "server") || testItems.some(item => item.kind === "server");
  const hasClients = testItems.some(item => item.kind === "client");
  const backends = [...new Set(capabilities
    .filter(item => ["server", "store", "broker", "data"].includes(capabilityKind(item) ?? ""))
    .map(item => item.name))].sort((left, right) => left.localeCompare(right));
  const seen = new Set([...runItems, ...testItems].flatMap(item => item.changes.map(change => change.source)));
  const sources = (["testside", "observed", "applicationside"] as ChangeSource[]).filter(source => seen.has(source));
  return {
    hosting: hasServer ? "in-process" : hasClients ? "remote" : "unknown",
    capabilities,
    backends,
    sources,
    applicationInstrumented: seen.has("applicationside")
  };
}

