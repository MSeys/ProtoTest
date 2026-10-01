import { existsSync, readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { describe, expect, it } from "vitest";
import { checkItems, deriveVisibility, diagnosisRule, findFailure, shapeMismatches, untracedGaps } from "./analysis";
import { openTraceArchive } from "./archive";
import { buildRun } from "./model";
import type { ChangeSource, Item, SectionItem, Span, TestTrace } from "./model";

function span(overrides: Partial<Span>): Span {
  return {
    id: "span", parent: null, children: [], depth: 0, name: "span", kind: "test.execution", source: "ProtoTest.Core",
    phase: "execution", status: "succeeded", error: null, start: 0, duration: 1, end: 1, count: 1,
    attributes: {}, sections: [], moments: [], evidence: [], itemKey: null, item: null, changes: [], test: null,
    ...overrides
  };
}

function testTrace(spans: Span[]): TestTrace {
  return {
    number: 1, id: "t1", name: "Suite.Test", className: "Suite", method: "Test", outcome: "failed",
    duration: 10, start: 0, end: 10, spans, roots: [], byId: new Map(), moments: [], evidence: [],
    artifacts: new Map(), items: [], failure: null
  };
}

function verdict(label: string, tone: SectionItem["tone"]): SectionItem {
  return { label, value: "1", detail: null, tone };
}

function item(kind: string, name: string, state: Record<string, string> = {}, sources: ChangeSource[] = []): Item {
  return {
    key: `${kind}:${name}`, kind, id: name, name, scope: null, firstSeen: 0, lastSeen: 0, state,
    changes: sources.map(source => ({
      at: 0, change: "changed", state: {}, source, inferred: false, item: undefined as unknown as Item, span: null
    })),
    test: null
  };
}

describe("checkItems", () => {
  it("returns only the Checks sections' items, in order", () => {
    const target = span({
      sections: [
        { label: "Result", kind: "checks", items: [verdict("status", "success")], content: null, language: null },
        { label: "Body", kind: "code", items: [verdict("ignored", "neutral")], content: "{}", language: "json" },
        { label: "Extra", kind: "checks", items: [verdict("errors", "error")], content: null, language: null }
      ]
    });

    expect(checkItems(target).map(entry => entry.label)).toEqual(["status", "errors"]);
  });
});

describe("shapeMismatches", () => {
  it("reads the recorded mismatch list when the check wrote one", () => {
    const target = span({
      attributes: {
        "shape.mismatches": JSON.stringify([{ propertyPath: "$.id", reason: "values did not match", expected: 1, actual: 2 }])
      }
    });

    expect(shapeMismatches(target)).toEqual([{ path: "$.id", reason: "values did not match", expected: 1, actual: 2 }]);
  });

  it("falls back to a diff section for a third-party check", () => {
    const target = span({
      sections: [{
        label: "Diff", kind: "diff", content: null, language: null,
        items: [{ label: "$.name", value: "atlas", detail: "northstar", tone: "error" }]
      }]
    });

    expect(shapeMismatches(target)).toEqual([{ path: "$.name", reason: "", expected: "atlas", actual: "northstar" }]);
  });
});

describe("findFailure", () => {
  it("prefers a check over the phase span that contains it and finds the call it judged", () => {
    const call = span({ id: "call", kind: "http.request", status: "succeeded" });
    const check = span({
      id: "check", kind: "assert.http.status", status: "failed", depth: 2, parent: call,
      sections: [{ label: "Result", kind: "checks", items: [verdict("status", "error")], content: null, language: null }]
    });
    const phase = span({ id: "phase", kind: "test.execution", status: "failed", children: [call] });
    call.parent = phase;
    call.children = [check];

    const failure = findFailure(testTrace([phase, call, check]));

    expect(failure?.span.id).toBe("check");
    expect(failure?.check?.label).toBe("status");
    expect(failure?.call?.id).toBe("call");
  });

  it("does not let a deeper cancelled child outrank the failed operation", () => {
    const failed = span({
      id: "failed", kind: "http.request", status: "failed", depth: 1,
      error: { type: "TimeoutException", message: "the request timed out" }
    });
    const cancelled = span({
      id: "cancelled", kind: "http.request", status: "cancelled", depth: 2, parent: failed,
      error: { type: "OperationCanceledException", message: "the retry was cancelled" }
    });
    failed.children = [cancelled];

    const failure = findFailure(testTrace([span({ id: "phase", kind: "test.execution", status: "failed" }), failed, cancelled]));

    expect(failure?.span.id).toBe("failed");
  });

  it("returns null when nothing failed", () => {
    expect(findFailure(testTrace([span({})]))).toBeNull();
  });
});

describe("deriveVisibility", () => {
  it("reports hosting, sorted backends and the sources in wire order", () => {
    const items = [
      item("capability", "Northstar", { "capability.kind": "server" }),
      item("capability", "Postgres", { "capability.kind": "store" }),
      item("value", "invoice", {}, ["applicationside", "testside"])
    ];
    const test = { ...testTrace([]), items: [item("client", "Api", {}, ["observed"])] };

    const visibility = deriveVisibility(items, [test]);

    expect(visibility.hosting).toBe("in-process");
    expect(visibility.backends).toEqual(["Northstar", "Postgres"]);
    expect(visibility.sources).toEqual(["testside", "observed", "applicationside"]);
    expect(visibility.applicationInstrumented).toBe(true);
  });
});

/** Walks up from the test's working directory so the file is found from the viewer or the repository root. */
function repositoryFile(relative: string): string {
  let directory = process.cwd();
  for (;;) {
    const candidate = join(directory, relative);
    if (existsSync(candidate)) return candidate;
    const parent = dirname(directory);
    if (parent === directory) throw new Error(`Could not find ${relative} from ${process.cwd()}.`);
    directory = parent;
  }
}

describe("findFailure over the committed demo trace", () => {
  it("selects the failures the diagnosis and the CLI pin", async () => {
    const bytes = readFileSync(repositoryFile("viewer/public/demos/prototest-demo.prototrace"));
    const buffer = bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) as ArrayBuffer;

    const archive = await openTraceArchive(buffer);
    const run = buildRun(archive.spans, archive.state);
    const failureOf = (name: string) => run.tests.find(test => test.name.endsWith(name))?.failure;

    expect(failureOf("ARealWaitDoesNotCloseTheDueWindow")?.span.kind).toBe("assert.json.shape");
    expect(failureOf("ABareStatusHidesWhatTheApplicationSaid")?.span.kind).toBe("assert.http.status");
    expect(failureOf("AnUnknownProjectIdIsTreatedAsMine")?.span.kind).toBe("assert.http.status");
    expect(failureOf("TheAddressWasHardcodedForOneMachine")?.span.kind).toBe("test.execution");

    // The shape check was judged on a protocol call, so the failure names that call; the address drill
    // threw before any call was recorded.
    expect(failureOf("ARealWaitDoesNotCloseTheDueWindow")?.call?.kind).toBe("http.request");
    expect(failureOf("TheAddressWasHardcodedForOneMachine")?.call).toBeNull();
  });
});

// Time with no operation is stated, not left as empty space: in the demo it is the cause of two failures.
describe("untracedGaps and diagnosisRule over the committed demo trace", () => {
  async function demoRun() {
    const bytes = readFileSync(repositoryFile("viewer/public/demos/prototest-demo.prototrace"));
    const buffer = bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) as ArrayBuffer;
    const archive = await openTraceArchive(buffer);
    return buildRun(archive.spans, archive.state);
  }
  const byMethod = (tests: TestTrace[], method: string) => tests.find(test => test.method === method)!;

  it("finds the real wait before the call that judged it, and the call that never ran", async () => {
    const run = await demoRun();
    const wait = untracedGaps(byMethod(run.tests, "ARealWaitDoesNotCloseTheDueWindow"));
    expect(wait).toHaveLength(1);
    expect(wait[0].phase).toBe("execution");
    expect(Math.round(wait[0].duration)).toBe(1006);
    expect(wait[0].before?.name).toBe("REST · GET /api/v1/organization");

    const hardcoded = untracedGaps(byMethod(run.tests, "TheAddressWasHardcodedForOneMachine"));
    expect(hardcoded).toHaveLength(1);
    expect(Math.round(hardcoded[0].duration)).toBe(2029);
    expect(hardcoded[0].before).toBeNull();
  });

  it("stays quiet about the ordinary cost between steps", async () => {
    const run = await demoRun();
    const all = run.tests.flatMap(test => untracedGaps(test));
    expect(all).toHaveLength(3);
  });

  it("names each outcome with the rule the CLI summary uses", async () => {
    const run = await demoRun();
    expect(diagnosisRule(byMethod(run.tests, "ARealWaitDoesNotCloseTheDueWindow"))).toBe("assertion");
    expect(diagnosisRule(byMethod(run.tests, "TheAddressWasHardcodedForOneMachine"))).toBe("runner-failure");
    expect(diagnosisRule(byMethod(run.tests, "APassingJourneyCanStillCarryAWarning"))).toBe("finding");
    expect(diagnosisRule(byMethod(run.tests, "CreatingAProjectReturnsIt"))).toBeNull();
  });
});

describe("diagnosisRule", () => {
  it("names a failed operation that is not a check an operation error", () => {
    const call = span({ kind: "http.request", status: "failed", error: { type: "HttpRequestException", message: "refused" } });
    const test = testTrace([call]);
    test.failure = findFailure(test);
    expect(diagnosisRule(test)).toBe("operation-error");
  });
});
