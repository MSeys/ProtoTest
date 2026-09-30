import { describe, expect, it, vi } from "vitest";
import { createApp, h, nextTick, type VNode } from "vue";
import RunView from "./RunView.vue";
import type { Gate, Item, Run, Span, TestTrace } from "../trace/model";

/** The smallest run-level operation the attention list needs: a name, a kind and a failed status. */
function span(overrides: Partial<Span>): Span {
  return {
    id: "span", parent: null, children: [], depth: 0, name: "Release the broker", kind: "resource.release",
    source: "ProtoTest", phase: "run", status: "failed", error: { type: "Error", message: "Broker refused to close" },
    start: 0, duration: 1, end: 1, count: 1, attributes: {}, sections: [], moments: [], evidence: [], itemKey: null,
    item: null, changes: [], test: null, ...overrides
  };
}

/** The smallest test the run strip needs: a number, a method name and an outcome. */
function testTrace(number: number, outcome: TestTrace["outcome"]): TestTrace {
  return {
    number, id: `t${number}`, name: `Suite.Journey.Test${number}`, className: "Suite.Journey", method: `Test${number}`,
    outcome, duration: 10, start: number * 10, end: number * 10 + 10, spans: [], roots: [], byId: new Map(),
    moments: [], evidence: [], artifacts: new Map(), items: [], failure: null
  };
}

function run(tests: TestTrace[], overrides: Partial<Run> = {}): Run {
  const outcomes = tests.map(test => test.outcome);
  return {
    id: "run", start: 0, end: 100, duration: 100, environment: {}, tests, spans: [], moments: [], evidence: [],
    artifacts: new Map(), items: [], gates: [], findings: [],
    visibility: { hosting: "in-process", capabilities: [], backends: [], sources: [], applicationInstrumented: true },
    counts: {
      succeeded: outcomes.filter(outcome => outcome === "succeeded").length,
      partial: outcomes.filter(outcome => outcome === "partial").length,
      failed: outcomes.filter(outcome => outcome === "failed").length,
      cancelled: outcomes.filter(outcome => outcome === "cancelled").length,
      skipped: outcomes.filter(outcome => outcome === "skipped").length,
      unknown: outcomes.filter(outcome => outcome === "unknown").length
    },
    ...overrides
  };
}

function mount(view: VNode) {
  const host = document.createElement("div");
  document.body.append(host);
  const app = createApp({ render: () => view });
  app.mount(host);
  return { host, unmount: () => { app.unmount(); host.remove(); } };
}

// The run strip is interactive, so it reads as a group of named buttons, not as one image.
describe("RunView run strip", () => {
  it("labels every test button instead of hiding the strip behind an image role", () => {
    const host = document.createElement("div");
    document.body.append(host);
    const select = vi.fn();
    const tests = [testTrace(1, "succeeded"), testTrace(2, "failed")];
    const app = createApp({
      render: () => h(RunView, { run: run(tests), fileName: "demo.prototrace", onSelect: select })
    });
    app.mount(host);

    const strip = host.querySelector<HTMLElement>(".strip");
    expect(strip?.getAttribute("role")).toBe("group");
    expect(strip?.getAttribute("aria-label")).toBe("2 tests, in start order");

    const ticks = [...host.querySelectorAll<HTMLButtonElement>(".tick")];
    expect(ticks.map(tick => tick.getAttribute("aria-label"))).toEqual([
      "01 Test1, succeeded",
      "02 Test2, failed"
    ]);
    ticks[1].click();
    expect(select).toHaveBeenCalledWith(tests[1]);

    app.unmount();
    host.remove();
  });

  // Skipped is planned, never run: its tick keeps its own class so it cannot read as a pass.
  it("gives a skipped test its own tick instead of the passing one", () => {
    const host = document.createElement("div");
    document.body.append(host);
    const tests = [testTrace(1, "succeeded"), testTrace(2, "skipped"), testTrace(3, "failed")];
    const app = createApp({
      render: () => h(RunView, { run: run(tests), fileName: "demo.prototrace", onSelect: () => {} })
    });
    app.mount(host);

    const ticks = [...host.querySelectorAll<HTMLButtonElement>(".tick")];
    expect(ticks.map(tick => tick.className)).toEqual([
      expect.stringContaining("success"),
      expect.stringContaining("neutral"),
      expect.stringContaining("danger")
    ]);

    app.unmount();
    host.remove();
  });
});

// Needs attention speaks in the outcome's own words, and the run's composition and gate details are stated.
describe("RunView needs attention", () => {
  // A cancelled test is not a partial one: the list names the outcome the trace recorded.
  it("names why a failing test failed in the test list", () => {
    const failed = span({ id: "op", name: "Assert status", kind: "assert.http.status", status: "failed", error: null });
    const tests = [testTrace(1, "failed")];
    tests[0].failure = { span: failed, check: null, mismatches: [], call: null };
    const passing = [testTrace(1, "succeeded")];
    const failedHost = mount(h(RunView, { run: run(tests), fileName: "demo.prototrace", onSelect: () => {} }));
    expect(failedHost.host.querySelector(".test-row .reason")?.textContent).toBe("Assert status");
    failedHost.unmount();
    const passingHost = mount(h(RunView, { run: run(passing), fileName: "demo.prototrace", onSelect: () => {} }));
    expect(passingHost.host.querySelector(".test-row .reason")).toBeNull();
    passingHost.unmount();
  });
});

// A failing run answers what failed before it explains what it could see.
describe("RunView order", () => {
  it("places needs attention before what the run could see", () => {
    const { host, unmount } = mount(h(RunView, {
      run: run([testTrace(1, "failed")]), fileName: "demo.prototrace", onSelect: () => {}
    }));

    const html = host.innerHTML;
    expect(html.indexOf("Needs attention")).toBeGreaterThan(-1);
    expect(html.indexOf("What this run could see")).toBeGreaterThan(-1);
    expect(html.indexOf("Needs attention")).toBeLessThan(html.indexOf("What this run could see"));
    unmount();
  });
});

// A cancelled test is not a partial one: the list names the outcome the trace recorded.
describe("RunView needs attention", () => {
  it("names a cancelled test by its own outcome instead of calling it partial", () => {
    const { host, unmount } = mount(h(RunView, {
      run: run([testTrace(1, "succeeded"), testTrace(2, "cancelled")]), fileName: "demo.prototrace", onSelect: () => {}
    }));

    const kinds = [...host.querySelectorAll(".attention .issue-kind")].map(entry => entry.textContent?.trim());

    expect(kinds).toEqual(["Cancelled"]);
    unmount();
  });

  it("states the backends the run composed and what a gate decided in detail", () => {
    const gate: Gate = {
      name: "No failures", status: "failed", message: "4 tests failed", details: "ProjectsJourney, ClockJourney",
      outcome: "failed", at: 50
    };
    const { host, unmount } = mount(h(RunView, {
      run: run([testTrace(1, "failed")], {
        gates: [gate],
        visibility: {
          hosting: "in-process", capabilities: [], backends: ["Northstar", "Postgres"], sources: [],
          applicationInstrumented: false
        }
      }),
      fileName: "demo.prototrace", onSelect: () => {}
    }));

    expect(host.textContent).toContain("Northstar, Postgres");
    const detail = host.querySelector(".issue.gate .issue-detail");
    expect(detail?.textContent).toBe("ProjectsJourney, ClockJourney");
    unmount();
  });

  // Each row sits on the run's own clock, so the clock is labelled once above the rows.
  it("labels the test list's time axis with the run's start and length", async () => {
    const { host, unmount } = mount(h(RunView, {
      run: run([testTrace(1, "succeeded")]), fileName: "demo.prototrace", onSelect: () => {}
    }));
    await nextTick();

    const axis = host.querySelector(".scale .axis");

    expect(axis?.textContent).toContain("start");
    expect(axis?.textContent).toContain("+100 ms");
    unmount();
  });

  // An outcome the verdict does not name is an outcome the reader cannot trust the verdict about.
  it("states unknown outcomes instead of dropping them", () => {
    const { host, unmount } = mount(h(RunView, {
      run: run([testTrace(1, "succeeded"), testTrace(2, "unknown")]),
      fileName: "demo.prototrace", onSelect: () => {}
    }));

    expect(host.querySelector(".headline h1")?.textContent).toContain("1 unknown");
    unmount();
  });
});

// A broken teardown belongs to no test, so without its own rows it would have no surface at all.
describe("RunView run problems", () => {
  it("surfaces a failed run-level release and an error moment as Run rows", () => {
    const release = span({});
    const { host, unmount } = mount(h(RunView, {
      run: run([testTrace(1, "succeeded")], {
        spans: [release],
        moments: [{
          at: 0, name: "Owned the broker", kind: "resource.owned", source: "ProtoTest", outcome: "failed",
          error: { type: "Error", message: "Broker never started" }, attributes: {}, sections: [], span: null
        }]
      }),
      fileName: "demo.prototrace", onSelect: () => {}
    }));

    const rows = [...host.querySelectorAll(".attention .issue.run")];
    expect(rows).toHaveLength(2);
    expect(rows.map(row => row.querySelector("b")?.textContent)).toEqual(["Run", "Run"]);
    expect(rows[0].textContent).toContain("Release the broker");
    expect(rows[0].textContent).toContain("Broker refused to close");
    expect(rows[1].textContent).toContain("Broker never started");
    unmount();
  });
});

// A finding that names the operation it came from opens the test at that operation.
describe("RunView findings", () => {
  it("opens a finding at its operation, and one without an operation at the test landing", () => {
    const tests = [testTrace(1, "failed")];
    const operation = span({ id: "op-1", name: "Assert status", kind: "assert.http.status", status: "failed", error: null, test: tests[0] });
    const select = vi.fn();
    const { host, unmount } = mount(h(RunView, {
      run: run(tests, {
        findings: [
          {
            finding: {
              type: "finding", at: 0, message: "Extra fields", status: "Warning", category: null,
              target: null, tags: [], metadata: {}, span: operation
            },
            test: tests[0]
          },
          {
            finding: {
              type: "finding", at: 0, message: "Loose warning", status: "Warning", category: null,
              target: null, tags: [], metadata: {}, span: null
            },
            test: tests[0]
          }
        ]
      }),
      fileName: "demo.prototrace", onSelect: select
    }));

    const rows = [...host.querySelectorAll<HTMLButtonElement>(".attention .issue.finding")];
    expect(rows).toHaveLength(2);
    rows[0].click();
    expect(select).toHaveBeenCalledWith(tests[0], { span: "op-1" });
    rows[1].click();
    expect(select).toHaveBeenCalledWith(tests[0], undefined);
    unmount();
  });
});

// The run owns resources that are not capabilities; the strip states what they are, in their own words.
describe("RunView resources", () => {
  function item(overrides: Partial<Item>): Item {
    return {
      key: "broker", kind: "broker", id: "messaging:broker", name: "Resource messaging:broker", scope: "run",
      firstSeen: 0, lastSeen: 1, state: { "resource.description": "Messaging broker", "resource.state": "released" },
      changes: [], test: null, ...overrides
    };
  }

  it("names the run's own resources from their recorded descriptions", () => {
    const { host, unmount } = mount(h(RunView, {
      run: run([testTrace(1, "succeeded")], { items: [item({})] }),
      fileName: "demo.prototrace", onSelect: () => {}
    }));

    expect(host.textContent).toContain("Messaging broker");
    expect(host.querySelector('[title="messaging:broker (released)"]')).toBeTruthy();
    unmount();
  });

  it("hides the resources row when the run holds only capabilities", () => {
    const { host, unmount } = mount(h(RunView, {
      run: run([testTrace(1, "succeeded")], {
        items: [item({ key: "capability", kind: "capability", id: "protocol:REST", name: "REST", state: {} })]
      }),
      fileName: "demo.prototrace", onSelect: () => {}
    }));

    expect(host.textContent).not.toContain("Resources");
    unmount();
  });
});
