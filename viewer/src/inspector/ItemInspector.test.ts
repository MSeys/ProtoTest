import { describe, expect, it } from "vitest";
import { createApp, h } from "vue";
import ItemInspector from "./ItemInspector.vue";
import type { Change, Item, TestTrace } from "../trace/model";

function testTrace(): TestTrace {
  return {
    number: 1, id: "t1", name: "Suite.Test", className: "Suite", method: "Test", outcome: "succeeded",
    duration: 10, start: 0, end: 10, spans: [], roots: [], byId: new Map(),
    moments: [], evidence: [], artifacts: new Map(), items: [], failure: null
  };
}

function resource(): Item {
  const item: Item = {
    key: "application:web", kind: "application", id: "web", name: "web", scope: "test", firstSeen: 1, lastSeen: 9,
    state: { "resource.id": "web", "resource.state": "released", "resource.release_ms": "0.3", "resource.owner": null },
    changes: [], test: null
  };
  const change = (at: number, name: string, state: Record<string, string | null>): Change =>
    ({ at, change: name, source: "testside", inferred: false, span: null, item, state } as Change);
  item.changes = [
    change(1, "created", { "resource.id": "web", "resource.state": "registered", "resource.owner": null }),
    change(9, "released", { "resource.state": "released", "resource.release_ms": "0.3" })
  ];
  return item;
}

function mount(item: Item) {
  const host = document.createElement("div");
  const app = createApp({ render: () => h(ItemInspector, { item, test: testTrace() }) });
  app.mount(host);
  return { host, unmount: () => app.unmount() };
}

const text = (element: Element | null | undefined) => element?.textContent?.replace(/\s+/g, " ").trim();

// Properties read as properties: the namespace once, short keys, values as text with their unit.
describe("ItemInspector state", () => {
  it("says the shared namespace once and shows values without JSON quotes", () => {
    const { host, unmount } = mount(resource());

    expect(text(host.querySelector("h3 small"))).toBe("resource");
    expect([...host.querySelectorAll(".fields dt")].map(entry => entry.textContent)).toEqual(["id", "state", "release_ms", "owner"]);
    expect([...host.querySelectorAll(".fields dd")].map(entry => entry.textContent)).toEqual(["web", "released", "0.3 ms", "null"]);
    expect(host.querySelector(".fields dd.null")?.textContent).toBe("null");
    unmount();
  });
});

// A change shows what it changed; the change that created the item folds the values the table already holds.
describe("ItemInspector changes", () => {
  it("folds the opening values and shows later changes as before and after", () => {
    const { host, unmount } = mount(resource());
    const [created, released] = [...host.querySelectorAll(".trail li")];

    expect(created.querySelector(".line strong")?.textContent).toBe("created");
    expect(text(created.querySelector(".line .by"))).toBe("Test side");
    expect(text(created.querySelector("details.values summary"))).toBe("with 3 values");
    expect(created.querySelector<HTMLDetailsElement>("details.values")?.open).toBe(false);

    expect([...released.querySelectorAll(".diff dt")].map(entry => entry.textContent)).toEqual(["state", "release_ms"]);
    const [state, release] = released.querySelectorAll(".diff dd");
    expect([state.querySelector(".before")?.textContent, state.querySelector(".after")?.textContent]).toEqual(["registered", "released"]);
    expect([release.querySelector(".after")?.textContent, release.querySelector(".new")?.textContent]).toEqual(["0.3 ms", "new"]);
    expect(release.querySelector(".before")).toBeNull();
    unmount();
  });
});
