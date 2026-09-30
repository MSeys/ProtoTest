import { describe, expect, it } from "vitest";
import { createApp, h, nextTick, type VNode } from "vue";
import StoryRow from "./StoryRow.vue";
import { story } from "../trace/story";
import type { Span, TestTrace } from "../trace/model";

function span(overrides: Partial<Span>): Span {
  return {
    id: "call", parent: null, children: [], depth: 0, name: "Create project", kind: "http.request",
    source: "ProtoTest.Rest", phase: "execution", status: "succeeded", error: null,
    start: 0, duration: 5, end: 5, count: 1, attributes: {}, sections: [], moments: [],
    evidence: [
      { type: "observation", at: 4, target: "projects", kind: "row-count", identifier: null, data: null, metadata: {}, span: null },
      { type: "attachment", at: 5, name: "response.json", artifact: null, span: null }
    ],
    itemKey: null, item: null, changes: [], test: null, ...overrides
  };
}

function testTrace(call: Span): TestTrace {
  return {
    number: 1, id: "t1", name: "Suite.Test", className: "Suite", method: "Test", outcome: "succeeded",
    duration: 10, start: 0, end: 10, spans: [call], roots: [call], byId: new Map([[call.id, call]]),
    moments: [], evidence: [], artifacts: new Map(), items: [], failure: null
  };
}

function mount(view: VNode) {
  const host = document.createElement("div");
  document.body.append(host);
  const app = createApp({ render: () => view });
  app.mount(host);
  return { host, unmount: () => { app.unmount(); host.remove(); } };
}

// What the operation stated reads where it happened, so the row's count names only the files it left.
describe("StoryRow observations", () => {
  it("states the observation inline and counts the files in words", async () => {
    const call = span({});
    const test = testTrace(call);
    const row = story(test)[0].rows[0];
    const { host, unmount } = mount(h(StoryRow, {
      row, test, depth: 0, open: new Set<string>(), onSelect: () => {}, onToggle: () => {}
    }));
    await nextTick();

    expect(host.querySelector(".observed")?.textContent?.replace(/\s+/g, " ")).toContain("Observed row-count on projects");
    expect(host.querySelector(".marks")?.textContent?.trim()).toBe("1 file");
    unmount();
  });
});

// Time the trace cannot account for is a row of its own, saying how long and where it ended.
describe("StoryRow gaps", () => {
  it("states a gap with its length and the operation that ended it", async () => {
    const lifecycle = span({ id: "exec", name: "Test execution", kind: "test.execution", start: 0, duration: 1200, end: 1200, evidence: [] });
    const call = span({ id: "call", parent: lifecycle, depth: 1, start: 1100, duration: 5, end: 1105, evidence: [] });
    lifecycle.children = [call];
    const test: TestTrace = { ...testTrace(call), duration: 1200, end: 1200, spans: [lifecycle, call], roots: [lifecycle], byId: new Map([["exec", lifecycle], ["call", call]]) };
    const gapRow = story(test)[0].rows[0];
    expect(gapRow.type).toBe("gap");
    const { host, unmount } = mount(h(StoryRow, {
      row: gapRow, test, depth: 0, open: new Set<string>(), onSelect: () => {}, onToggle: () => {}
    }));
    await nextTick();

    const text = host.querySelector(".gap")?.textContent?.replace(/\s+/g, " ") ?? "";
    expect(text).toContain("1.10 s with no recorded operation");
    expect(text).toContain("Until Create project started, +1.10 s into the test");
    unmount();
  });
});
