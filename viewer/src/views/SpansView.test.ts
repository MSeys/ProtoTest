import { describe, expect, it } from "vitest";
import { createApp, h, nextTick, type VNode } from "vue";
import SpansView from "./SpansView.vue";
import type { Span, TestTrace } from "../trace/model";

function span(overrides: Partial<Span>): Span {
  return {
    id: "span", parent: null, children: [], depth: 0, name: "span", kind: "http.request", source: "ProtoTest.Rest",
    phase: "execution", status: "succeeded", error: null, start: 0, duration: 1, end: 1, count: 1,
    attributes: {}, sections: [], moments: [], evidence: [], itemKey: null, item: null, changes: [], test: null,
    ...overrides
  };
}

function testTrace(spans: Span[]): TestTrace {
  const byId = new Map(spans.map(entry => [entry.id, entry]));
  return {
    number: 1, id: "t1", name: "Suite.Test", className: "Suite", method: "Test", outcome: "succeeded",
    duration: 10, start: 0, end: 10, spans, roots: spans.filter(entry => !entry.parent), byId,
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

// A search that hides most of the tree says how many spans matched, so the reader knows what they see.
describe("SpansView search", () => {
  it("counts the spans that match, not the ancestors kept for context", async () => {
    const call = span({ id: "call", name: "Create project" });
    const check = span({ id: "check", name: "Assert status", kind: "assert.http.status", parent: call, depth: 1 });
    const other = span({ id: "other", name: "Release resources", kind: "resources.release" });
    call.children = [check];
    const { host, unmount } = mount(h(SpansView, { test: testTrace([call, check, other]), onSelect: () => {} }));

    const input = host.querySelector<HTMLInputElement>("input[aria-label='Find a span']");
    expect(input).toBeTruthy();
    input!.value = "assert";
    input!.dispatchEvent(new Event("input", { bubbles: true }));
    await nextTick();

    // One span matches; its parent stays for context, so the list holds two rows but the count says one.
    expect(host.querySelector(".count")?.textContent).toBe("1 of 3 matches");
    expect(host.querySelectorAll(".row")).toHaveLength(2);
    unmount();
  });

  it("stays quiet when nothing is searched for", async () => {
    const { host, unmount } = mount(h(SpansView, { test: testTrace([span({})]), onSelect: () => {} }));
    await nextTick();

    expect(host.querySelector(".count")).toBeNull();
    unmount();
  });
});
