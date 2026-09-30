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

function setSearch(host: HTMLElement, value: string) {
  const input = host.querySelector<HTMLInputElement>("input[aria-label='Find a span']");
  expect(input).toBeTruthy();
  input!.value = value;
  input!.dispatchEvent(new Event("input", { bubbles: true }));
}

function attentionChip(host: HTMLElement): HTMLButtonElement {
  const chip = [...host.querySelectorAll<HTMLButtonElement>("button.chip")]
    .find(entry => entry.textContent?.includes("Needs attention"));
  expect(chip).toBeTruthy();
  return chip!;
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

// The search answers where a span is; the filter answers what needs attention, in the same words as the run.
describe("SpansView attention filter", () => {
  function failingTree(): TestTrace {
    const call = span({ id: "call", name: "Create project" });
    const check = span({ id: "check", name: "Assert status", kind: "assert.http.status", status: "failed", parent: call, depth: 1 });
    const other = span({ id: "other", name: "Release resources", kind: "resources.release" });
    call.children = [check];
    return testTrace([call, check, other]);
  }

  it("shows only the spans that need attention, with their ancestors for context", async () => {
    const { host, unmount } = mount(h(SpansView, { test: failingTree(), onSelect: () => {} }));
    await nextTick();

    attentionChip(host).click();
    await nextTick();

    // The failing check and the call above it stay; the unrelated release goes.
    expect(host.querySelector(".count")?.textContent).toBe("1 of 3 need attention");
    expect(host.querySelectorAll(".row")).toHaveLength(2);
    unmount();
  });

  it("combines with the search instead of replacing it", async () => {
    const { host, unmount } = mount(h(SpansView, { test: failingTree(), onSelect: () => {} }));
    await nextTick();

    attentionChip(host).click();
    await nextTick();
    setSearch(host, "release");
    await nextTick();

    // Nothing flagged matches: the list says exactly that, and offers the way back.
    expect(host.querySelector(".empty p")?.textContent).toBe("No flagged span matches this search.");
    const reset = [...host.querySelectorAll<HTMLButtonElement>("button.chip")]
      .find(entry => entry.textContent?.includes("Show all spans"));
    expect(reset).toBeTruthy();
    reset!.click();
    await nextTick();
    expect(host.querySelectorAll(".row")).toHaveLength(3);
    unmount();
  });

  it("says when nothing needs attention instead of showing an empty list", async () => {
    const { host, unmount } = mount(h(SpansView, { test: testTrace([span({})]), onSelect: () => {} }));
    await nextTick();

    attentionChip(host).click();
    await nextTick();

    expect(host.querySelector(".empty p")?.textContent).toBe("No span needs attention.");
    unmount();
  });

  it("says when the test recorded no spans at all", async () => {
    const { host, unmount } = mount(h(SpansView, { test: testTrace([]), onSelect: () => {} }));
    await nextTick();

    expect(host.querySelector(".empty p")?.textContent).toBe("This test recorded no spans.");
    unmount();
  });
});

// A span's leavings read in the model's own words: what it stated, captured or found, not one event count.
describe("SpansView evidence marks", () => {
  it("names observations, attachments and moments instead of lumping them as events", async () => {
    const call = span({
      id: "call", name: "Create project",
      evidence: [
        { type: "observation", at: 1, target: "projects", kind: "row-count", identifier: null, data: null, metadata: {}, span: null },
        { type: "attachment", at: 2, name: "shot.png", artifact: null, span: null }
      ],
      moments: [{ at: 3, name: "Server started", kind: "server.started", source: "ProtoTest", outcome: "succeeded", error: null, attributes: {}, sections: [], span: null }]
    });
    const { host, unmount } = mount(h(SpansView, { test: testTrace([call]), onSelect: () => {} }));
    await nextTick();

    const marks = host.querySelector(".row .events")?.textContent;
    expect(marks).toContain("1 observation");
    expect(marks).toContain("1 attachment");
    expect(marks).toContain("1 moment");
    expect(marks).not.toContain("event");
    unmount();
  });
});

// The tree is long and the search is its index: pressing / lands in it from anywhere outside a field.
describe("SpansView search shortcut", () => {  it("focuses the search on /", async () => {
    const { host, unmount } = mount(h(SpansView, { test: testTrace([span({})]), onSelect: () => {} }));
    await nextTick();

    const input = host.querySelector<HTMLInputElement>("input[aria-label='Find a span']");
    expect(input).toBeTruthy();
    host.querySelector(".list")!.dispatchEvent(new KeyboardEvent("keydown", { key: "/", bubbles: true }));
    await nextTick();

    expect(document.activeElement).toBe(input);
    unmount();
  });
});
