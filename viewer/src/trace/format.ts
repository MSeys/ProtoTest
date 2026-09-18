import type { ChangeSource, Item, Outcome, Phase, Span, TestTrace } from "./model";

export function formatDuration(ms: number): string {
  if (ms <= 0) return "0 ms";
  if (ms < 1) return `${Math.max(1, Math.round(ms * 1000))} µs`;
  if (ms < 1000) return `${ms.toFixed(ms < 10 ? 1 : 0)} ms`;
  return `${(ms / 1000).toFixed(2)} s`;
}

/** An offset inside a test, as a reader scans a timeline: +12 ms, +1.20 s. */
export function formatOffset(ms: number): string {
  return `+${formatDuration(Math.max(0, ms))}`;
}

export function formatDate(at: number): string {
  return new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "medium" }).format(new Date(at));
}

export function formatBytes(bytes: number | null | undefined): string {
  if (bytes === null || bytes === undefined) return "";
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

export function pad(value: number): string {
  return String(value).padStart(2, "0");
}

/** The outcome as the tone the design system colours: green only for verified, never decoration. */
export function tone(outcome: Outcome): "success" | "warning" | "danger" | "neutral" {
  if (outcome === "succeeded") return "success";
  if (outcome === "failed") return "danger";
  if (outcome === "partial" || outcome === "cancelled") return "warning";
  return "neutral";
}

export function outcomeLabel(outcome: Outcome): string {
  return outcome.charAt(0).toLocaleUpperCase() + outcome.slice(1);
}

export const sourceLabels: Record<ChangeSource, string> = {
  testside: "Test side",
  observed: "Observed",
  applicationside: "Application"
};

/** Acronyms and product names that stay whole and keep their casing when a name is split into words. */
const keptWords = ["GraphQL", "OpenAPI", "OAuth", "WebSocket", "JSON", "HTTPS", "HTTP", "REST", "API", "SQL", "URL", "UI", "ID", "CSV", "XML"];

/**
 * Turns a code name into words a person reads: `AFailedOperationRecordsItsDiagnostics` becomes
 * "A failed operation records its diagnostics". Acronyms keep their casing; a parameter suffix is kept as written.
 */
export function humanize(name: string): string {
  const suffixAt = name.indexOf("(");
  const base = suffixAt >= 0 ? name.slice(0, suffixAt) : name;
  const suffix = suffixAt >= 0 ? ` ${name.slice(suffixAt)}` : "";
  let text = base;
  keptWords.forEach((word, index) => { text = text.split(word).join(` ${index} `); });
  const words = text
    .replace(/_/g, " ")
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/([A-Z])([A-Z][a-z])/g, "$1 $2")
    .split(/\s+/)
    .filter(Boolean)
    .map(word => {
      const kept = /^(\d+)$/.exec(word);
      if (kept && keptWords[Number(kept[1])]) return keptWords[Number(kept[1])];
      const canonical = keptWords.find(candidate => candidate.toLocaleLowerCase() === word.toLocaleLowerCase());
      if (canonical) return canonical;
      return /^[A-Z0-9]{2,}$/.test(word) ? word : word.toLocaleLowerCase();
    });
  if (!words.length) return name;
  words[0] = words[0].charAt(0).toLocaleUpperCase() + words[0].slice(1);
  return `${words.join(" ")}${suffix}`;
}

/** The method name with any parameter suffix, as the runner named the test. */
export function testCodeName(test: TestTrace): string {
  const prefix = test.className ? `${test.className}.${test.method}` : test.method;
  const suffix = test.name.startsWith(prefix) ? test.name.slice(prefix.length) : "";
  return `${test.method}${suffix}`;
}

export function testTitle(test: TestTrace): string {
  return humanize(testCodeName(test));
}

export function testGroup(test: TestTrace): string {
  const className = test.className ?? "Other tests";
  return humanize(className.split(".").at(-1) || className);
}

export function testMatches(test: TestTrace, query: string): boolean {
  const text = query.trim().toLocaleLowerCase();
  if (!text) return true;
  return [testCodeName(test), testTitle(test), test.className ?? "", testGroup(test)]
    .some(value => value.toLocaleLowerCase().includes(text));
}

/** A type reads better without its namespace or generic arity: ProtoTest.Core.Internal.Hook`1 becomes Hook. */
export function shortType(value: string | null | undefined): string {
  if (!value) return "";
  const definition = value.replace(/`\d+(\[\[.*)?$/s, "");
  return definition.split(".").at(-1) || definition;
}

export interface KindLabel {
  /** The execution-vocabulary token: --type-<id>, falling back to --type-custom for an unknown kind. */
  id: string;
  label: string;
}

/** What kind of thing a span is, in the words a reader uses. An unknown kind gets its own first word. */
export function kindLabel(kind: string): KindLabel {
  if (kind.startsWith("hook.") || kind.startsWith("attribute.")) return { id: "extension", label: "Extension" };
  if (kind === "http.request" || kind === "graphql.operation") return { id: "call", label: "Call" };
  if (kind.startsWith("assert.")) return { id: "assertion", label: "Check" };
  if (kind.startsWith("data.")) return { id: "data", label: "Data" };
  if (kind.startsWith("attachment.")) return { id: "artifact", label: "Artifact" };
  if (kind.startsWith("resource")) return { id: "ownership", label: "Ownership" };
  if (kind.startsWith("client.")) return { id: "client", label: "Client" };
  if (kind.startsWith("context.")) return { id: "context", label: "Context" };
  if (kind.startsWith("auth.")) return { id: "auth", label: "Auth" };
  if (kind.startsWith("test.")) return { id: "lifecycle", label: "Phase" };
  if (kind.startsWith("gate.")) return { id: "gate", label: "Gate" };
  if (kind.startsWith("graphql.")) return { id: "call", label: "GraphQL" };
  const first = kind.split(/[.\s]/)[0] || "Step";
  return { id: "custom", label: first.charAt(0).toLocaleUpperCase() + first.slice(1) };
}

/** The facts a row carries next to its name: a call's status, a check's verdict, what a data step made. */
export function spanFacts(span: Span): string {
  const attributes = span.attributes;
  if (span.kind === "http.request") {
    const status = attributes["http.response.status_code"];
    return status ? `status ${status}` : "";
  }
  if (span.kind === "graphql.operation") return attributes["graphql.operation.type"] ?? "";
  if (span.kind.startsWith("assert.")) {
    const check = span.sections.flatMap(section => section.kind === "checks" ? section.items : [])[0];
    return check?.value ?? "";
  }
  if (span.kind.startsWith("hook.") || span.kind.startsWith("attribute.")) return "";
  return "";
}

export function isCheck(span: Span): boolean {
  return span.kind.startsWith("assert.");
}

export interface PhaseSegment {
  phase: Phase;
  outcome: Outcome;
  start: number;
  duration: number;
}

/** Where each phase of a test ran, from its lifecycle spans: the bars of the run timeline. */
export function phaseSegments(test: TestTrace): PhaseSegment[] {
  return test.roots
    .filter(span => span.kind === `test.${span.phase}`)
    .map(span => ({ phase: span.phase, outcome: span.status, start: span.start, duration: span.duration }));
}

/** An item by the name it has in its own state (`project.name`), falling back to the name the trace gave it. */
export function itemTitle(item: Item): string {
  return item.state[`${item.kind.toLocaleLowerCase()}.name`] ?? item.name;
}

/** A tracked item's kind as a chip: the framework's own kinds keep their family, a domain object reads as data. */
export function itemKindLabel(item: Item): KindLabel {
  const families: Record<string, string> = { client: "client", context: "context", auth: "auth", server: "ownership", database: "ownership", capability: "extension" };
  const label = item.kind.charAt(0).toLocaleUpperCase() + item.kind.slice(1);
  return { id: families[item.kind.toLocaleLowerCase()] ?? "data", label };
}
