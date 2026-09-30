import { describe, expect, it } from "vitest";
import { createApp, h, nextTick, type VNode } from "vue";
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

  // A filter that matches nothing leaves the reader somewhere; the list offers the way back.
  it("offers a way back from a search that matches nothing", async () => {
    const { host, unmount } = mount(h(TestRail, {
      tests: [testTrace(1, "succeeded")], onSelect: () => {}
    }));

    const input = host.querySelector<HTMLInputElement>("input[type='search']")!;
    input.value = "zzz";
    input.dispatchEvent(new Event("input", { bubbles: true }));
    await nextTick();
    expect(host.querySelector(".rail-row")).toBeNull();

    const reset = [...host.querySelectorAll("button")]
      .find(entry => entry.textContent?.trim() === "Show all tests");
    expect(reset).toBeTruthy();
    reset!.click();
    await nextTick();
    expect(host.querySelector(".rail-row")).toBeTruthy();
    unmount();
  });

  // The status dot is silent, so the open test is marked and every row states its outcome in words.
  it("marks the selected row as current and names each outcome", () => {
    const tests = [testTrace(1, "succeeded"), testTrace(2, "failed")];
    const { host, unmount } = mount(h(TestRail, { tests, selected: tests[1], onSelect: () => {} }));

    const rows = [...host.querySelectorAll(".rail-row")];
    expect(rows.map(row => row.getAttribute("aria-current"))).toEqual([null, "true"]);
    expect(rows[0].textContent).toContain("Succeeded");
    expect(rows[1].textContent).toContain("Failed");
    unmount();
  });
});
