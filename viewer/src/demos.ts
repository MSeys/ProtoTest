import { openTraceArchive } from "./trace/archive";
import { buildRun, type Outcome } from "./trace/model";

/** One bundled demo: the file under public/demos/, the label the list shows, and what it shows. */
export interface DemoEntry {
  key: string;
  file: string;
  label: string;
  description: string;
  /** The two product runs lead the list; the recipes are the quieter group under them. */
  group: "run" | "recipe";
}

/*
 * The bundled demos. Adding one is a single entry here plus the .prototrace file under
 * public/demos/: the empty state lists every entry, and ?demo=<key> opens it.
 */
export const demos: DemoEntry[] = [
  {
    key: "full",
    file: "prototest-demo.prototrace",
    label: "ProtoTest demo trace",
    description: "A full Northstar run: passing tests alongside failures, a partial, and a finding.",
    group: "run"
  },
  {
    key: "opencsms",
    file: "opencsms.prototrace",
    label: "OpenCSMS demo trace",
    description: "A full OpenCSMS product run: API contracts, billing, OCPP charging journeys, and operator-dashboard browser tests.",
    group: "run"
  },
  {
    key: "rest-graphql",
    file: "recipes/rest-graphql.prototrace",
    label: "REST to GraphQL recipe",
    description: "One REST write read back through GraphQL, matching the docs recipe.",
    group: "recipe"
  },
  {
    key: "rest-database",
    file: "recipes/rest-database.prototrace",
    label: "REST to database recipe",
    description: "One REST write committed to the database, matching the docs recipe.",
    group: "recipe"
  },
  {
    key: "workbook",
    file: "recipes/workbook.prototrace",
    label: "Workbook recipe",
    description: "One report matched against its spreadsheet model, matching the docs recipe.",
    group: "recipe"
  }
];

/* ?demo=1 is the historic address of the full demo; every other demo opens by key. */
export function resolveDemo(requested: string | null | undefined): DemoEntry {
  if (!requested) return demos[0];
  if (requested === "1") return demos[0];
  return demos.find(entry => entry.key === requested) ?? demos[0];
}

export function demoFileUrl(entry: DemoEntry): string {
  return `${import.meta.env.BASE_URL}demos/${entry.file}`;
}

/** The honest facts the empty state shows per demo, read from the trace itself. */
export interface DemoFacts {
  tests: number;
  failed: number;
  partial: number;
  cancelled: number;
  /** Every test's outcome in start order, so the entry can draw the run's own shape. */
  outcomes: Outcome[];
}

export async function summarizeDemo(buffer: ArrayBuffer): Promise<DemoFacts> {
  const opened = await openTraceArchive(buffer);
  const run = buildRun(opened.spans, opened.state);
  return {
    tests: run.tests.length,
    failed: run.counts.failed,
    partial: run.counts.partial,
    cancelled: run.counts.cancelled,
    outcomes: run.tests.map(test => test.outcome)
  };
}

/** The facts in one line, in the run view's order: failures first, then what stopped short. */
export function formatDemoFacts(facts: DemoFacts): string {
  const parts = [`${facts.tests} ${facts.tests === 1 ? "test" : "tests"}`];
  if (facts.failed) parts.push(`${facts.failed} failed`);
  if (facts.partial) parts.push(`${facts.partial} partial`);
  if (facts.cancelled) parts.push(`${facts.cancelled} cancelled`);
  return parts.join(" · ");
}
