import type { ShapeCheckNode, ShapeCheckStatus, ShapeMismatch, TestTrace, TraceEntry, TraceOutcome, TracePhase, TraceTreeItem } from "./trace-schema";

export const phaseOrder: TracePhase[] = ["Setup", "Execution", "Rollback", "Teardown", "Run"];

export const traceCategories = [
  { id: "hooks", label: "Hooks & attributes", glyph: "HK" },
  { id: "contexts", label: "Contexts", glyph: "CX" },
  { id: "clients", label: "Clients", glyph: "CL" },
  { id: "auth", label: "Authentication", glyph: "AU" },
  { id: "requests", label: "Requests", glyph: "RQ" },
  { id: "assertions", label: "Assertions", glyph: "AS" },
  { id: "observations", label: "Observations", glyph: "OB" },
  { id: "artifacts", label: "Artifacts", glyph: "AR" }
] as const;

export type TraceCategory = string;

export function milliseconds(value: string | number | null | undefined): number {
  if (!value) return 0;
  if (typeof value === "number") return value;
  const match = /(?:(\d+):)?(\d+):(\d+(?:\.\d+)?)/.exec(value);
  return match ? ((Number(match[1] || 0) * 3600 + Number(match[2]) * 60 + Number(match[3])) * 1000) : 0;
}

export function formatDuration(ms: number): string {
  if (ms < 1) return `${Math.round(ms * 1000)} µs`;
  if (ms < 1000) return `${ms.toFixed(ms < 10 ? 1 : 0)} ms`;
  return `${(ms / 1000).toFixed(2)} s`;
}

export function formatDate(value: string): string {
  return new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "medium" }).format(new Date(value));
}

export function runDuration(tests: TestTrace[]): number {
  if (!tests.length) return 0;
  const start = Math.min(...tests.map(test => Date.parse(test.startedAtUtc)));
  const end = Math.max(...tests.map(test => Date.parse(test.startedAtUtc) + milliseconds(test.duration)));
  return end - start;
}

export function tone(outcome: TraceOutcome): string {
  return outcome === "Succeeded" ? "success" : outcome === "Failed" ? "danger" : outcome === "Partial" || outcome === "Cancelled" ? "warning" : "neutral";
}

export function pad(value: number): string { return String(value).padStart(2, "0"); }

export function testDisplayName(test: TestTrace): string {
  const method = test.methodName || test.name.split(".").at(-1) || test.name;
  const parameterSuffix = test.name.startsWith(`${test.className}.${method}`)
    ? test.name.slice(`${test.className}.${method}`.length)
    : "";
  return `${method}${parameterSuffix}`;
}

export function testGroupName(test: TestTrace): string {
  const className = test.className ?? "Other tests";
  return className.split(".").at(-1) || className;
}

export function testGroupNamespace(test: TestTrace): string {
  const className = test.className ?? "";
  const separator = className.lastIndexOf(".");
  return separator > 0 ? className.slice(0, separator) : "";
}

export function categoryForEntry(entry: TraceEntry): TraceCategory {
  const kind = entry.kind;
  if (kind.startsWith("test.")) return "lifecycle";
  if (kind.startsWith("hook.") || kind.startsWith("attribute.")) return "hooks";
  if (kind.startsWith("context.")) return "contexts";
  if (kind.startsWith("auth.")) return "auth";
  if (kind.startsWith("assert.")) return "assertions";
  if (kind.startsWith("observation.")) return "observations";
  if (kind.startsWith("attachment.")) return "artifacts";
  if (kind.startsWith("client.") || kind.startsWith("http.client.") || kind.startsWith("aspnetcore.")) return "clients";
  if (kind.startsWith("http.") || kind.startsWith("rest.") || kind.startsWith("graphql.")) return "requests";
  return kind.split(".")[0] || "other";
}

export function categoryDefinitions(entries: TraceEntry[]) {
  const known = new Map<string, { id: string; label: string; glyph: string }>(traceCategories.map(category => [category.id, category]));
  for (const entry of entries) {
    const id = categoryForEntry(entry);
    if (id === "lifecycle" || known.has(id)) continue;
    known.set(id, {
      id,
      label: id === "other" ? "Other diagnostics" : `${id.charAt(0).toLocaleUpperCase()}${id.slice(1)}`,
      glyph: id.slice(0, 2).toLocaleUpperCase().padEnd(2, "·")
    });
  }
  return [...known.values()];
}

export function overviewEntries(category: TraceCategory, entries: TraceEntry[]): TraceEntry[] {
  const categoryEntries = entries.filter(entry => categoryForEntry(entry) === category);
  const failed = categoryEntries.filter(entry => entry.outcome === "Failed" || entry.error);
  const preferred = categoryEntries.filter(entry => {
    if (category === "hooks") return ["hook.before", "attribute.before"].includes(entry.kind);
    if (category === "contexts") return entry.kind === "context.set";
    if (category === "clients") return entry.kind === "client.initialize";
    if (category === "auth") return ["auth.apply", "auth.skip", "auth.disable"].includes(entry.kind);
    if (category === "requests") return ["http.request", "graphql.operation"].includes(entry.kind);
    if (category === "artifacts") return entry.kind === "attachment.register";
    if (category === "assertions" || category === "observations") return true;
    return entry.entryKind === "Operation";
  });
  const selected = preferred.length ? [...preferred, ...failed] : categoryEntries;
  return [...new Map(selected.map(entry => [entry.id, entry])).values()];
}

export function entrySummary(entry: TraceEntry): string {
  const attributes = entry.attributes ?? {};
  const values = [
    attributes["shape.result"] === "mismatched" ? `${attributes["shape.mismatch_count"] ?? "?"} mismatches` : null,
    attributes["shape.result"] === "matched" ? `${attributes["matched.property_count"] ?? "?"} properties matched` : null,
    attributes["client.name"],
    attributes["context.type"],
    attributes["hook.type"],
    attributes["attribute.type"],
    attributes["auth.type"],
    [attributes["http.request.method"], attributes["http.route"] ?? attributes["server.address"]].filter(Boolean).join(" "),
    [attributes["graphql.operation.type"], attributes["graphql.operation.name"]].filter(Boolean).join(" "),
    attributes["observation.kind"],
    attributes["attachment.name"],
    attributes["actual.status_code"] ? `status ${attributes["actual.status_code"]}` : null,
    attributes["matched.property_count"] ? `${attributes["matched.property_count"]} properties matched` : null
  ].filter((value): value is string => Boolean(value));
  if (entry.error?.message) return entry.error.message;
  if (values.length) return [...new Set(values)].slice(0, 2).join(" · ");
  return `${entry.kind} · ${entry.entryKind.toLocaleLowerCase()}`;
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

export function relativeTime(entry: TraceEntry, test: TestTrace): string {
  const offset = Math.max(0, Date.parse(entry.timestampUtc) - Date.parse(test.startedAtUtc));
  return `+${formatDuration(offset)}`;
}

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

function shapePathParts(path: string): string[] {
  const normalized = path.trim().replace(/^\$\.?/, "");
  return normalized.match(/[^.[\]]+|\[\d+\]/g) ?? [];
}

function shapeValueAt(value: unknown, path: string): unknown {
  let current = value;
  for (const part of shapePathParts(path)) {
    if (current === null || typeof current !== "object") return undefined;
    const key = part.startsWith("[") ? Number(part.slice(1, -1)) : part;
    current = (current as Record<string | number, unknown>)[key];
  }
  return current;
}

export function buildShapeTree(matches: string[], mismatches: ShapeMismatch[], expected?: unknown, actual?: unknown): ShapeCheckNode[] {
  const root: ShapeCheckNode = { key: "$", path: "$", status: "branch", children: [] };
  const add = (path: string, status: "matched" | "failed", mismatch?: ShapeMismatch) => {
    let parent = root;
    let fullPath = "$";
    for (const part of shapePathParts(path)) {
      fullPath += part.startsWith("[") ? part : `.${part}`;
      let child = parent.children.find(item => item.key === part);
      if (!child) {
        child = { key: part, path: fullPath, status: "branch", children: [] };
        parent.children.push(child);
      }
      parent = child;
    }
    parent.status = status;
    parent.mismatch = mismatch;
    parent.expected = mismatch?.expected ?? shapeValueAt(expected, path);
    parent.actual = mismatch?.actual ?? shapeValueAt(actual, path);
  };

  for (const path of matches) add(path, "matched");
  for (const mismatch of mismatches) add(mismatch.propertyPath ?? mismatch.path ?? "$", "failed", mismatch);

  const complete = (node: ShapeCheckNode): ShapeCheckStatus => {
    const childStatuses = node.children.map(complete);
    if (node.status === "failed" || childStatuses.includes("failed")) return node.status = "failed";
    if (node.status === "matched" || (childStatuses.length && childStatuses.every(status => status === "matched"))) return node.status = "matched";
    return node.status = "branch";
  };
  root.children.forEach(complete);
  return root.children;
}

export function matchesView(entry: TraceEntry, view: string): boolean {
  if (view === "flow") return entry.entryKind === "Operation" || entry.outcome === "Failed" || Boolean(entry.error);
  if (view === "trace") return true;
  if (view === "assertions") return entry.kind.startsWith("assert.");
  if (view === "network") return entry.kind.startsWith("http.") || entry.kind === "graphql.operation" || entry.kind === "graphql.endpoint.resolve";
  if (view === "observations") return entry.kind.startsWith("observation.");
  if (view === "artifacts") return entry.kind.startsWith("attachment.");
  if (view === "errors") return entry.outcome === "Failed" || Boolean(entry.error);
  return true;
}
