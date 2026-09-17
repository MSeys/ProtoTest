import type { TestTrace, TraceEntry, TraceOutcome } from "./trace-schema";

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

export function pad(value: number): string { return String(value).padStart(2, "0"); }

export function tone(outcome: TraceOutcome): string {
  return outcome === "Succeeded" ? "success" : outcome === "Failed" ? "danger" : outcome === "Partial" || outcome === "Cancelled" ? "warning" : "neutral";
}

/** A type reads better without its namespace: ProtoTest.Core.Internal.ProtoClientInitializerHook. */
export function shortenType(value: string | null | undefined): string | undefined {
  if (!value) return undefined;
  return value.split(".").at(-1) || value;
}

/** Prefers the description an integration recorded over the name ProtoTest generated for the entry. */
export function entryTitle(entry: TraceEntry): string {
  return entry.attributes?.["resource.description"]
    ?? entry.attributes?.["attachment.name"]
    ?? entry.attributes?.["gate.name"]
    ?? entry.name;
}

export function entrySummary(entry: TraceEntry): string {
  const attributes = entry.attributes ?? {};
  const values = [
    attributes["shape.result"] === "mismatched" ? `${attributes["shape.mismatch_count"] ?? "?"} mismatches` : null,
    attributes["shape.result"] === "matched" ? `${attributes["matched.property_count"] ?? "?"} properties matched` : null,
    attributes["client.name"],
    shortenType(attributes["context.type"]),
    shortenType(attributes["hook.type"]),
    shortenType(attributes["attribute.type"]),
    shortenType(attributes["auth.type"]),
    [attributes["http.request.method"], attributes["http.route"] ?? attributes["server.address"]].filter(Boolean).join(" "),
    [attributes["graphql.operation.type"], attributes["graphql.operation.name"]].filter(Boolean).join(" "),
    attributes["observation.kind"],
    attributes["attachment.name"],
    attributes["resource.description"],
    attributes["gate.name"],
    attributes["gate.status"],
    attributes["actual.status_code"] ? `status ${attributes["actual.status_code"]}` : null,
    attributes["matched.property_count"] ? `${attributes["matched.property_count"]} properties matched` : null
  ].filter((value): value is string => Boolean(value));
  if (entry.error?.message) return entry.error.message;
  if (values.length) return [...new Set(values)].slice(0, 2).join(" · ");
  return `${entry.kind} · ${entry.entryKind.toLocaleLowerCase()}`;
}

export function relativeTime(entry: TraceEntry, test: TestTrace): string {
  const offset = Math.max(0, Date.parse(entry.timestampUtc) - Date.parse(test.startedAtUtc));
  return `+${formatDuration(offset)}`;
}

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
