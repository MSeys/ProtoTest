import { beforeEach, describe, expect, it, vi } from "vitest";
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

describe("StateView shared clock", () => {
  it("places the ruler, phases, lifeline and changes on the same test axis", () => {
    const entry = item({ firstSeen: 102, lastSeen: 108 });
    entry.changes = [change({ at: 105, item: entry })];
    const test = { ...testTrace([entry]), start: 100, end: 110 };
    const execution = span({ kind: "test.execution", start: 102, duration: 6, end: 108 });
    test.spans = [execution]; test.roots = [execution];
    const { host, unmount } = mount(h(StateView, { test }));
    expect(host.querySelector(".scale")?.getAttribute("aria-label")).toBe("Test clock, 0 to 10 ms");
    expect(host.querySelector<HTMLElement>(".phases i")?.style.left).toBe("20%");
    expect(host.querySelector<HTMLElement>(".phases i")?.style.width).toBe("60%");
    expect(host.querySelector<HTMLElement>(".life")?.style.left).toBe("20%");
    expect(host.querySelector<HTMLElement>(".life")?.style.width).toBe("60%");
    expect(host.querySelector<HTMLElement>("button.tick")?.style.left).toBe("50%");
    expect(host.querySelector<HTMLButtonElement>("button.tick")?.disabled).toBe(true);
    unmount();
  });

  it("shades the selected operation on every lifeline and highlights only the items it touched", async () => {
    const changed = item({}); const actedOn = item({ key: "context", kind: "context", id: "ctx" }); const other = item({ key: "other", id: "other" });
    const operation = span({ id: "op", name: "Load project", start: 2, duration: 3, end: 5, item: actedOn });
    changed.changes = [change({ at: 3, item: changed, span: operation })];
    const test = testTrace([changed, actedOn, other]);
    test.spans = [operation]; test.byId.set(operation.id, operation);
    const select = vi.fn();
    const { host, unmount } = mount(h(StateView, { test, selectedSpan: operation.id, onSelectSpan: select }));
    await nextTick();
    expect(host.querySelectorAll(".selected-time")).toHaveLength(3);
    expect(host.querySelector<HTMLElement>(".selected-time")?.style.left).toBe("20%");
    expect(host.querySelectorAll(".item.related")).toHaveLength(2);
    expect(host.querySelector(".selection")?.textContent).toContain("Load project, +2.0 ms to +5.0 ms");
    host.querySelector<HTMLButtonElement>("button.tick")!.click();
    expect(select).toHaveBeenCalledWith(operation);
    unmount();
  });

  it("keeps item selection visible and opens its inspector", () => {
    const entry = item({}); const select = vi.fn();
    const { host, unmount } = mount(h(StateView, { test: testTrace([entry]), selected: { kind: entry.kind, id: entry.id }, onSelectItem: select }));
    expect(host.querySelector(".item.active .name")?.getAttribute("aria-pressed")).toBe("true");
    host.querySelector<HTMLButtonElement>("button.name")!.click();
    expect(select).toHaveBeenCalledWith(entry);
    unmount();
  });

  it("does not stretch a zero-time lifetime across the whole test", () => {
    const { host, unmount } = mount(h(StateView, { test: testTrace([item({ firstSeen: 0, lastSeen: 0 })]) }));
    expect(host.querySelector<HTMLElement>(".life")?.style.width).toBe("0.6%");
    unmount();
  });

  it("explains empty state without an invented operation selection", async () => {
    const { host, unmount } = mount(h(StateView, { test: testTrace([]), selectedSpan: "missing" }));
    await nextTick();
    expect(host.textContent).toContain("This test tracked no state");
    expect(host.querySelector(".selection")).toBeNull();
    unmount();
  });
});
