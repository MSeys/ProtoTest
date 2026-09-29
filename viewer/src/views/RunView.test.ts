import { describe, expect, it, vi } from "vitest";
import { createApp, h, nextTick, type VNode } from "vue";
import RunView from "./RunView.vue";
import type { Gate, Run, TestTrace } from "../trace/model";

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
});
