import { beforeEach, describe, expect, it } from "vitest";
import { createApp, h, nextTick, type VNode } from "vue";
import StateView from "./StateView.vue";
import type { Change, Item, Span, TestTrace } from "../trace/model";

function span(overrides: Partial<Span>): Span {
  return {
    id: "span", parent: null, children: [], depth: 0, name: "span", kind: "http.request", source: "ProtoTest.Rest",
    phase: "execution", status: "succeeded", error: null, start: 0, duration: 1, end: 1, count: 1,
    attributes: {}, sections: [], moments: [], evidence: [], itemKey: null, item: null, changes: [], test: null,
    ...overrides
  };
}

function item(overrides: Partial<Item>): Item {
  return {
    key: "client:c1", kind: "client", id: "c1", name: "Northstar", scope: "test",
    firstSeen: 0, lastSeen: 10, state: { "client.name": "Northstar" }, changes: [], test: null,
    ...overrides
  };
}

function change(overrides: Partial<Change>): Change {
  return {
    at: 1, change: "created", state: {}, source: "testside", inferred: false,
    item: undefined as unknown as Item, span: null, ...overrides
  };
}

function testTrace(items: Item[]): TestTrace {
  return {
    number: 1, id: "t1", name: "Suite.Test", className: "Suite", method: "Test", outcome: "succeeded",
    duration: 10, start: 0, end: 10, spans: [], roots: [], byId: new Map(),
    moments: [], evidence: [], artifacts: new Map(), items, failure: null
  };
}

function mount(view: VNode) {
  const host = document.createElement("div");
  document.body.append(host);
  const app = createApp({ render: () => view });
  app.mount(host);
  return { host, unmount: () => { app.unmount(); host.remove(); } };
}

beforeEach(() => {
  Element.prototype.scrollIntoView = function () {};
});

// A tick is the link back to the timeline; when its operation is selected the row says the cause in words.
describe("StateView cause", () => {
  function traced(): { test: TestTrace; operation: Span } {
    const operation = span({ id: "op-1", name: "Create project" });
    const entry = item({});
    const caused = change({ change: "created", item: entry, span: operation });
    entry.changes = [caused];
    operation.changes = [caused];
    return { test: testTrace([entry]), operation };
  }

  it("names the operation that caused the selected change", async () => {
    const { test, operation } = traced();
    const { host, unmount } = mount(h(StateView, {
      test, selectedSpan: operation.id, onSelectItem: () => {}, onSelectSpan: () => {}
    }));
    await nextTick();

    expect(host.querySelector(".tick.active")).toBeTruthy();
    expect(host.querySelector(".cause")?.textContent).toBe("created by Create project");
    unmount();
  });

  it("stays quiet with no selection", async () => {
    const { test } = traced();
    const { host, unmount } = mount(h(StateView, { test, onSelectItem: () => {}, onSelectSpan: () => {} }));
    await nextTick();

    expect(host.querySelector(".cause")).toBeNull();
    unmount();
  });

  it("renders an inferred change hollow, not as a record", async () => {
    const operation = span({ id: "op-1", name: "Create project" });
    const entry = item({});
    entry.changes = [change({ item: entry, span: operation, inferred: true })];
    const { host, unmount } = mount(h(StateView, { test: testTrace([entry]), onSelectItem: () => {}, onSelectSpan: () => {} }));
    await nextTick();

    expect(host.querySelector(".tick.inferred")).toBeTruthy();
    unmount();
  });
});

// A row says how much changed, so the reader sees the churn before opening the trail.
describe("StateView change count", () => {
  it("names the number of changes on the row", async () => {
    const entry = item({});
    const first = change({ change: "created", item: entry, at: 1 });
    const second = change({ change: "released", item: entry, at: 5 });
    entry.changes = [first, second];
    const { host, unmount } = mount(h(StateView, { test: testTrace([entry]), onSelectItem: () => {}, onSelectSpan: () => {} }));
    await nextTick();

    expect(host.querySelector(".name .changes")?.textContent).toBe("2 changes");
    unmount();
  });
});
