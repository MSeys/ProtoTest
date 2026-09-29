import { describe, expect, it } from "vitest";
import { createApp, h, type VNode } from "vue";
import TestRail from "./TestRail.vue";
import type { Span, TestTrace } from "../trace/model";

function span(overrides: Partial<Span>): Span {
  return {
    id: "span", parent: null, children: [], depth: 0, name: "Assert status", kind: "assert.http.status",
    source: "ProtoTest.Rest", phase: "execution", status: "failed", error: null, start: 0, duration: 1, end: 1,
    count: 1, attributes: {}, sections: [], moments: [], evidence: [], itemKey: null, item: null, changes: [],
    test: null, ...overrides
  };
}

function testTrace(number: number, outcome: TestTrace["outcome"], failure: TestTrace["failure"] = null): TestTrace {
  return {
    number, id: `t${number}`, name: `Suite.Test${number}`, className: "Suite", method: `Test${number}`,
    outcome, duration: 10, start: number * 10, end: number * 10 + 10, spans: [], roots: [], byId: new Map(),
    moments: [], evidence: [], artifacts: new Map(), items: [], failure
  };
}

function mount(view: VNode) {
  const host = document.createElement("div");
  document.body.append(host);
  const app = createApp({ render: () => view });
  app.mount(host);
  return { host, unmount: () => { app.unmount(); host.remove(); } };
}

// A test that did not pass says why in the list. Without a failing operation the outcome is still
// stated in words: never colour alone.
describe("TestRail reasons", () => {
  it("names the outcome when no failing operation recorded one", () => {
    const { host, unmount } = mount(h(TestRail, {
      tests: [testTrace(1, "succeeded"), testTrace(2, "cancelled")], onSelect: () => {}
    }));

    const reasons = [...host.querySelectorAll(".rail-row .reason")].map(entry => entry.textContent?.trim());
    expect(reasons).toEqual(["Cancelled"]);
    unmount();
  });

  it("names the failing check when the trace recorded one", () => {
    const failed = span({});
    const { host, unmount } = mount(h(TestRail, {
      tests: [testTrace(1, "failed", { span: failed, check: null, mismatches: [], call: null })],
      onSelect: () => {}
    }));

    expect(host.querySelector(".rail-row .reason")?.textContent?.trim()).toBe("Assert status");
    unmount();
  });
});
