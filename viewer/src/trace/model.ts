import type {
  WireArtifact, WireAttributes, WireChange, WireError, WireEvent, WireItem, WireResourceGroup, WireRunAttributes,
  WireSection, WireSpans, WireState, WireTestAttributes
} from "./wire";

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

/** Kind and id together name an item; a unit separator cannot occur in either. */
const separator = String.fromCharCode(31);
function itemKey(kind: string, id: string): string {
  return kind + separator + id;
}

const outcomes: Outcome[] = ["succeeded", "partial", "failed", "cancelled", "skipped", "unknown"];

function outcome(value: string | null | undefined): Outcome {
  const lower = (value ?? "").toLowerCase();
  return (outcomes as string[]).includes(lower) ? lower as Outcome : "unknown";
}

function phase(value: string): Phase {
  const lower = value.toLowerCase();
  return lower === "run" || lower === "setup" || lower === "execution" || lower === "rollback" || lower === "teardown" ? lower : "execution";
}

function tone(value: string): Tone {
  const lower = value.toLowerCase();
  return lower === "success" || lower === "warning" || lower === "error" ? lower : "neutral";
}

function changeSource(value: string): ChangeSource {
  const lower = value.toLowerCase();
  return lower === "observed" || lower === "applicationside" ? lower : "testside";
}

function time(value: string | null | undefined): number {
  const parsed = value ? Date.parse(value) : NaN;
  return Number.isFinite(parsed) ? parsed : 0;
}

function sections(value: WireSection[] | null | undefined): Section[] {
  return (value ?? []).map(section => ({
    label: section.label,
    kind: section.kind.toLowerCase(),
    items: (section.items ?? []).map(item => ({ label: item.label, value: item.value, detail: item.detail, tone: tone(item.tone) })),
    content: section.content,
    language: section.language
  }));
}

function artifacts(group: WireResourceGroup): Map<string, Artifact> {
  return new Map((group.artifacts ?? []).map((artifact: WireArtifact) => [artifact.id, { ...artifact }]));
}

function isTestGroup(group: WireResourceGroup): group is WireResourceGroup & { resource: { attributes: WireTestAttributes } } {
  return "testId" in group.resource.attributes;
}

/** Splits a wire event into a timeline moment or a piece of evidence, resolving an attachment's artifact. */
function readEvent(
  event: WireEvent,
  span: Span | null,
  artifactsById: Map<string, Artifact>,
  moments: Moment[],
  evidence: Evidence[]
) {
  const at = time(event.atUtc);
  switch (event.record) {
    case "observation":
      evidence.push({ type: "observation", at, target: event.name, kind: event.kind, identifier: event.identifier, data: event.data, metadata: event.metadata ?? {}, span });
      return;
    case "attachment":
      evidence.push({ type: "attachment", at, name: event.name, artifact: event.artifactId ? artifactsById.get(event.artifactId) ?? null : null, span });
      return;
    case "finding":
      evidence.push({ type: "finding", at, message: event.name, status: event.status, category: event.category, target: event.targetName, tags: event.tags ?? [], metadata: event.metadata ?? {}, span });
      return;
    default:
      moments.push({
        at, name: event.name, kind: event.kind, source: event.source, outcome: outcome(event.outcome), error: event.error,
        attributes: event.attributes ?? {}, sections: sections(event.sections), span
      });
  }
}

interface Built {
  spans: Span[];
  roots: Span[];
  byId: Map<string, Span>;
  moments: Moment[];
  evidence: Evidence[];
  artifacts: Map<string, Artifact>;
}

function buildSpans(group: WireResourceGroup, test: TestTrace | null): Built {
  const artifactsById = artifacts(group);
  // A resource may carry more than one instrumentation scope; every scope's spans and events belong
  // to the same test, so they are flattened instead of reading only the first scope.
  const scopes = group.scopeSpans ?? [];
  const wireSpans = scopes.flatMap(scope => scope.spans ?? []);
  const byId = new Map<string, Span>();
  const spans: Span[] = wireSpans.map(wire => {
    const start = time(wire.startedAtUtc);
    const duration = wire.durationMs ?? 0;
    const span: Span = {
      id: wire.spanId, parent: null, children: [], depth: 0, name: wire.name, kind: wire.kind, source: wire.source,
      phase: phase(wire.phase), status: outcome(wire.status), error: wire.error, start, duration, end: start + duration,
      count: wire.count ?? 1, attributes: wire.attributes ?? {}, sections: sections(wire.sections), moments: [], evidence: [],
      itemKey: wire.entityKind && wire.entityId ? itemKey(wire.entityKind, wire.entityId) : null, item: null, changes: [], test
    };
    byId.set(span.id, span);
    return span;
  });
  const roots: Span[] = [];
  wireSpans.forEach((wire, index) => {
    const span = spans[index];
    const parent = wire.parentSpanId ? byId.get(wire.parentSpanId) : undefined;
    if (parent && parent !== span) { span.parent = parent; parent.children.push(span); } else roots.push(span);
    for (const event of wire.events ?? []) readEvent(event, span, artifactsById, span.moments, span.evidence);
  });
  const setDepth = (span: Span, depth: number) => { span.depth = depth; span.children.forEach(child => setDepth(child, depth + 1)); };
  roots.forEach(root => setDepth(root, 0));
  const moments: Moment[] = [];
  const evidence: Evidence[] = [];
  for (const scope of scopes) {
    for (const event of scope.events ?? []) readEvent(event, null, artifactsById, moments, evidence);
  }
  return { spans, roots, byId, moments, evidence, artifacts: artifactsById };
}

function buildItems(wire: WireItem[], test: TestTrace | null, spansById: Map<string, Span>): Item[] {
  return wire.map(source => {
    const item: Item = {
      key: itemKey(source.kind, source.id), kind: source.kind, id: source.id, name: source.name, scope: source.scope,
      firstSeen: time(source.firstSeenUtc), lastSeen: time(source.lastSeenUtc), state: source.state ?? {}, changes: [], test
    };
    item.changes = (source.changes ?? []).map((change: WireChange) => {
      const span = change.operationId ? spansById.get(change.operationId) ?? null : null;
      const linked: Change = {
        at: time(change.atUtc), change: change.change, state: change.state ?? {}, source: changeSource(change.source),
        inferred: change.inferred, item, span
      };
      span?.changes.push(linked);
      return linked;
    }).sort((left, right) => left.at - right.at);
    return item;
  });
}

const callKinds = new Set(["http.request", "graphql.operation"]);

/** The deepest failing span, preferring a check, then anything with an error, over the phase that contains it. */
function findFailure(test: TestTrace): Failure | null {
  const failing = test.spans.filter(span => span.status === "failed" || span.error);
  if (!failing.length) return null;
  const score = (span: Span) =>
    span.depth * 10 + (span.error ? 100 : 0) + (span.kind.startsWith("assert.") ? 1000 : 0) + (span.kind.startsWith("test.") ? -500 : 0);
  const span = failing.reduce((best, candidate) => score(candidate) > score(best) ? candidate : best);
  const check = span.sections.flatMap(section => section.kind === "checks" ? section.items : []).find(item => item.tone === "error") ?? null;
  let call: Span | null = span;
  while (call && !callKinds.has(call.kind)) call = call.parent;
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

function deriveVisibility(runItems: Item[], tests: TestTrace[]): Visibility {
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

export function buildRun(spans: WireSpans, state: WireState): Run {
  const runGroup = spans.resourceSpans.find(group => !isTestGroup(group));
  const runAttributes = (runGroup?.resource.attributes ?? {}) as Partial<WireRunAttributes>;
  const stateByTest = new Map(state.tests.map(entry => [entry.testId, entry.items]));

  const tests: TestTrace[] = spans.resourceSpans.filter(isTestGroup).map(group => {
    const attributes = group.resource.attributes;
    const test: TestTrace = {
      number: 0, id: attributes.testId, name: attributes.testName, className: attributes.testClass,
      method: attributes.testMethod, outcome: outcome(attributes.testOutcome), duration: attributes.testDurationMs ?? 0,
      start: 0, end: 0, spans: [], roots: [], byId: new Map(), moments: [], evidence: [], artifacts: new Map(), items: [],
      failure: null
    };
    const built = buildSpans(group, test);
    Object.assign(test, built);
    test.start = built.spans.length ? Math.min(...built.spans.map(span => span.start)) : 0;
    test.end = test.start + test.duration;
    return test;
  });
  tests.sort((left, right) => left.start - right.start);
  tests.forEach((test, index) => { test.number = index + 1; });

  const runBuilt = runGroup ? buildSpans(runGroup, null) : { spans: [], roots: [], byId: new Map<string, Span>(), moments: [], evidence: [], artifacts: new Map<string, Artifact>() };
  const runItems = buildItems(state.run?.items ?? [], null, runBuilt.byId);
  for (const test of tests) {
    test.items = buildItems(stateByTest.get(test.id) ?? [], test, test.byId);
    // A test's own items win over the run's: a client the test initialized is the test's client.
    const byKey = new Map([...runItems, ...test.items].map(item => [item.key, item]));
    for (const span of test.spans) span.item = span.itemKey ? byKey.get(span.itemKey) ?? null : null;
    test.failure = findFailure(test);
  }

  const start = time(runAttributes.runStartedAtUtc) || (tests[0]?.start ?? 0);
  const end = time(runAttributes.runCompletedAtUtc) || Math.max(start, ...tests.map(test => test.end));
  const environment: Record<string, string> = {};
  for (const [key, value] of Object.entries(runAttributes)) {
    if (key.startsWith("environment.") && typeof value === "string") environment[key.slice("environment.".length)] = value;
  }

  const gates: Gate[] = runBuilt.moments.concat(runBuilt.spans.flatMap(span => span.moments))
    .filter(moment => moment.kind === "gate.evaluate")
    .map(moment => ({
      name: moment.attributes["gate.name"] ?? moment.name,
      status: moment.attributes["gate.status"] ?? moment.outcome,
      message: moment.attributes["gate.message"] ?? null,
      details: moment.attributes["gate.details"] ?? null,
      outcome: moment.outcome,
      at: moment.at
    }));

  const findings: Run["findings"] = [];
  const collect = (evidence: Evidence[], test: TestTrace | null) => {
    for (const item of evidence) if (item.type === "finding") findings.push({ finding: item, test });
  };
  collect(runBuilt.evidence, null);
  runBuilt.spans.forEach(span => collect(span.evidence, null));
  for (const test of tests) {
    collect(test.evidence, test);
    test.spans.forEach(span => collect(span.evidence, test));
  }

  const counts = Object.fromEntries(outcomes.map(value => [value, 0])) as Record<Outcome, number>;
  tests.forEach(test => { counts[test.outcome] += 1; });

  return {
    id: runAttributes.runId ?? "", start, end, duration: Math.max(0, end - start), environment, tests,
    spans: runBuilt.spans, moments: runBuilt.moments, evidence: runBuilt.evidence, artifacts: runBuilt.artifacts,
    items: runItems, gates, findings, visibility: deriveVisibility(runItems, tests), counts
  };
}
