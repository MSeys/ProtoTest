import type { TraceEntry } from "./trace-schema";

/**
 * The design context's verbosity model: Normal is the meaningful lifecycle and integration work, Detailed
 * adds provisioning and resolution, Diagnostic is the resolver detail that only helps when something is
 * wrong. The story view shows the normal level and folds the rest onto the step that owns it.
 */
export type TraceLevel = "normal" | "detailed" | "diagnostic";

const detailedKinds = new Set([
  "test.setup", "test.execution", "test.rollback", "test.teardown",
  "client.register", "client.try_resolve", "client.initializer.attempt",
  "context.set", "context.resolve", "context.try_resolve", "context.dispose",
  "auth.apply", "auth.skip", "auth.disable",
  "http.route.resolve", "http.body.configure",
  "rest.builder.create", "rest.context.configure",
  "graphql.context.configure", "graphql.endpoint.resolve",
  "data.build"
]);

export function entryLevel(entry: TraceEntry): TraceLevel {
  // A failure is worth showing at every level: the reader asks where it failed and what preceded it.
  if (entry.outcome === "Failed" || entry.outcome === "Cancelled" || entry.error) return "normal";
  const kind = entry.kind;
  // Starting an application is the most expensive thing a setup does; it belongs in the story.
  if (kind === "aspnetcore.server.initialize") return "normal";
  if (kind.startsWith("aspnetcore.") || kind.startsWith("graphql.schema.")) return "diagnostic";
  if (detailedKinds.has(kind)) return "detailed";
  if (/^(context|auth)\./.test(kind)) return "detailed";
  // Creating a client is part of the story; resolving one again is plumbing.
  if (kind.startsWith("client.")) return kind === "client.initialize" ? "normal" : "detailed";
  if (kind.startsWith("data.")) return kind === "data.create" || kind === "data.provision" ? "normal" : "detailed";
  if (kind.startsWith("http.")) return kind === "http.request" ? "normal" : "detailed";
  if (kind.startsWith("rest.")) return "detailed";
  if (kind.startsWith("graphql.")) return kind === "graphql.operation" ? "normal" : "detailed";
  return "normal";
}

/** Assertions, observations, artifacts and findings belong to the operation they describe. */
export function isEvidenceEntry(entry: TraceEntry): boolean {
  return entry.kind.startsWith("assert.")
    || entry.kind.startsWith("observation.")
    || entry.kind.startsWith("attachment.")
    || entry.kind.startsWith("finding.");
}

/** The lenses are filters over the same execution: the whole story, the orchestration, or only what went wrong. */
export function lensMatches(entry: TraceEntry, lens: string): boolean {
  if (lens === "lifecycle") return /^(test|hook|attribute)\./.test(entry.kind);
  if (lens === "diagnostics") {
    return entry.outcome === "Failed" || entry.outcome === "Cancelled" || entry.outcome === "Partial"
      || Boolean(entry.error) || /^(finding|gate)\./.test(entry.kind);
  }
  return true;
}

/** What kind of thing a node is, in the words a reader uses: a call, a data step, an assertion. */
export function nodeType(entry: TraceEntry): { id: string; label: string } {
  const kind = entry.kind;
  if (kind.startsWith("hook.") || kind.startsWith("attribute.")) return { id: "extension", label: "Extension" };
  if (kind.startsWith("data.")) return { id: "data", label: "Data" };
  if (kind === "http.request" || kind === "graphql.operation" || kind === "graphql.endpoint.resolve") return { id: "call", label: "Call" };
  if (kind.startsWith("assert.")) return { id: "assertion", label: "Assertion" };
  if (kind.startsWith("observation.")) return { id: "observation", label: "Observation" };
  if (kind.startsWith("attachment.")) return { id: "artifact", label: "Artifact" };
  if (kind.startsWith("resource.")) return { id: "ownership", label: "Ownership" };
  if (kind.startsWith("client.")) return { id: "client", label: "Client" };
  if (kind.startsWith("finding.")) return { id: "finding", label: "Finding" };
  if (kind.startsWith("gate.")) return { id: "gate", label: "Gate" };
  if (kind.startsWith("test.")) return { id: "lifecycle", label: "Lifecycle" };
  if (kind.startsWith("context.")) return { id: "context", label: "Context" };
  if (kind.startsWith("auth.")) return { id: "auth", label: "Authentication" };
  const group = kind.split(".")[0] || "step";
  return { id: group, label: `${group.charAt(0).toLocaleUpperCase()}${group.slice(1)}` };
}

/** The short facts a reader wants on a node: the route and status of a call, the count of a shape check, the size of an artifact. */
export function nodeFacts(entry: TraceEntry): string[] {
  const attributes = entry.attributes ?? {};
  const facts: string[] = [];
  const add = (value: string | null | undefined) => { if (value) facts.push(value); };
  if (entry.kind === "http.request") {
    add([attributes["http.request.method"], attributes["http.route"]].filter(Boolean).join(" "));
    add(attributes["http.response.status_code"] ? `status ${attributes["http.response.status_code"]}` : null);
  } else if (entry.kind === "graphql.operation") {
    add([attributes["graphql.operation.type"], attributes["graphql.operation.name"]].filter(Boolean).join(" "));
  } else if (entry.kind.startsWith("assert.")) {
    if (attributes["shape.result"] === "mismatched") add(`${attributes["shape.mismatch_count"] ?? "?"} mismatches`);
    else if (attributes["shape.result"] === "matched") add(`${attributes["matched.property_count"] ?? "?"} properties matched`);
    add(attributes["actual.status_code"] ? `status ${attributes["actual.status_code"]}` : null);
  } else if (entry.kind.startsWith("attachment.")) {
    add(attributes["attachment.media_type"]);
    add(attributes["attachment.size_bytes"] ? `${attributes["attachment.size_bytes"]} bytes` : null);
  } else if (entry.kind.startsWith("observation.")) {
    add(attributes["observation.kind"]);
  } else if (entry.kind.startsWith("resource.")) {
    add(attributes["resource.kind"]);
  } else if (entry.kind.startsWith("finding.")) {
    add(attributes["finding.status"]);
    add(attributes["finding.category"]);
  } else if (entry.kind.startsWith("gate.")) {
    add(attributes["gate.status"]);
  } else if (entry.kind.startsWith("hook.") || entry.kind.startsWith("attribute.")) {
    add(shortType(attributes["hook.type"] ?? attributes["attribute.type"]));
  } else if (entry.kind.startsWith("client.")) {
    add(shortType(attributes["client.type"]));
  }
  if (!facts.length) add(entry.error?.message ?? entry.kind);
  return facts.slice(0, 2);
}

function shortType(value: string | null | undefined): string | undefined {
  if (!value) return undefined;
  return value.split(".").at(-1) || value;
}

/** Progressive detail: zooming in reveals structure, then diagnostics. */
export function lodFor(zoom: number): 1 | 2 | 3 {
  return zoom < 1.15 ? 1 : zoom < 1.6 ? 2 : 3;
}

export function lodLabel(lod: 1 | 2 | 3): string {
  return lod === 1 ? "Story" : lod === 2 ? "Structure" : "Evidence";
}

/** The coarse bucket an entry belongs to, used for the inspector's kicker chip. */
export function categoryForEntry(entry: TraceEntry): string {
  const kind = entry.kind;
  if (kind.startsWith("test.")) return "lifecycle";
  if (kind.startsWith("hook.") || kind.startsWith("attribute.")) return "hooks";
  if (kind.startsWith("context.")) return "contexts";
  if (kind.startsWith("auth.")) return "auth";
  if (kind.startsWith("assert.")) return "assertions";
  if (kind.startsWith("observation.")) return "observations";
  if (kind.startsWith("attachment.")) return "artifacts";
  if (kind.startsWith("resource.")) return "resources";
  if (kind.startsWith("finding.") || kind.startsWith("gate.")) return "findings";
  if (kind.startsWith("client.") || kind.startsWith("http.client.") || kind.startsWith("aspnetcore.")) return "clients";
  if (kind.startsWith("http.") || kind.startsWith("rest.") || kind.startsWith("graphql.")) return "requests";
  return kind.split(".")[0] || "other";
}

/** What an evidence entry did, as a verb, so a story row reads as a sentence. */
export function evidenceRole(entry: TraceEntry): string {
  const kind = entry.kind;
  if (kind.startsWith("assert.")) return entry.outcome === "Failed" ? "failed" : "asserted";
  if (kind.startsWith("observation.")) return "observed";
  if (kind.startsWith("attachment.")) return "attached";
  if (kind.startsWith("finding.")) return "found";
  return "recorded";
}
