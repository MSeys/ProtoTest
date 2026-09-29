import { describe, expect, it, vi } from "vitest";
import { createApp, h } from "vue";
import RunView from "./RunView.vue";
import type { Run, TestTrace } from "../trace/model";

/** The smallest test the run strip needs: a number, a method name and an outcome. */
function testTrace(number: number, outcome: TestTrace["outcome"]): TestTrace {
  return {
    number, id: `t${number}`, name: `Suite.Journey.Test${number}`, className: "Suite.Journey", method: `Test${number}`,
    outcome, duration: 10, start: number * 10, end: number * 10 + 10, spans: [], roots: [], byId: new Map(),
    moments: [], evidence: [], artifacts: new Map(), items: [], failure: null
  };
}

function run(tests: TestTrace[]): Run {
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
    }
  };
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
