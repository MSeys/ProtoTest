import type { ChangeSource, Failure, Item, SectionItem, ShapeMismatch, Span, TestTrace, Visibility } from "./model";

const callKinds = new Set(["http.request", "graphql.operation"]);

/** The verdict items of a span's Checks sections, in order. */
export function checkItems(span: Span) {
  return span.sections.flatMap(section => section.kind === "checks" ? section.items : []);
}

/** The deepest failing span, preferring a check, then anything with an error, over the phase that contains it. */
export function findFailure(test: TestTrace): Failure | null {
  const failing = test.spans.filter(span => span.status === "failed" || span.error);
  if (!failing.length) return null;
  const score = (span: Span) =>
    span.depth * 10 + (span.error ? 100 : 0) + (span.kind.startsWith("assert.") ? 1000 : 0) + (span.kind.startsWith("test.") ? -500 : 0);
  const span = failing.reduce((best, candidate) => score(candidate) > score(best) ? candidate : best);
  const check = checkItems(span).find(item => item.tone === "error") ?? null;
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

