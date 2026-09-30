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

// What the operation stated reads where it happened; the counts say what they are in words, not glyphs.
describe("StoryRow observations", () => {
  it("states the observation inline and labels the evidence counts", async () => {
    const call = span({});
    const test = testTrace(call);
    const row = story(test)[0].rows[0];
    const { host, unmount } = mount(h(StoryRow, {
      row, test, depth: 0, open: new Set<string>(), onSelect: () => {}, onToggle: () => {}
    }));
    await nextTick();

    expect(host.querySelector(".observed")?.textContent).toContain("Observed row-count on projects");
    const marks = host.querySelector(".marks")?.textContent ?? "";
    expect(marks).toContain("1 observation");
    expect(marks).toContain("1 attachment");
    expect(marks).not.toContain("◎");
    expect(marks).not.toContain("⧉");
    unmount();
  });
});
