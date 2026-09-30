import { describe, expect, it } from "vitest";
import { createApp, h, nextTick, type VNode } from "vue";
import TimelineView from "./TimelineView.vue";
import type { Span, TestTrace } from "../trace/model";

function span(overrides: Partial<Span>): Span {
  return {
    id: "span", parent: null, children: [], depth: 0, name: "span", kind: "http.request", source: "ProtoTest.Rest",
    phase: "execution", status: "succeeded", error: null, start: 0, duration: 1, end: 1, count: 1,
    attributes: {}, sections: [], moments: [], evidence: [], itemKey: null, item: null, changes: [], test: null,
    ...overrides
  };
}

function testTrace(spans: Span[], duration = 10): TestTrace {
  const byId = new Map(spans.map(entry => [entry.id, entry]));
  return {
    number: 1, id: "t1", name: "Suite.Test", className: "Suite", method: "Test", outcome: "succeeded",
    duration, start: 0, end: duration, spans, roots: spans.filter(entry => !entry.parent), byId,
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
  const input = host.querySelector<HTMLInputElement>("input[aria-label='Find an operation']");
  expect(input).toBeTruthy();
  input!.value = value;
  input!.dispatchEvent(new Event("input", { bubbles: true }));
}

function chip(host: HTMLElement, label: string): HTMLButtonElement {
  const found = [...host.querySelectorAll<HTMLButtonElement>("button.chip")].find(entry => entry.textContent?.trim().startsWith(label));
  expect(found, label).toBeTruthy();
  return found!;
}

const names = (host: HTMLElement) => [...host.querySelectorAll(".row:not(.gap) .name")].map(entry => entry.textContent);

function failingTree(): TestTrace {
  const call = span({ id: "call", name: "Create project" });
  const check = span({ id: "check", name: "Assert status", kind: "assert.http.status", status: "failed", parent: call, depth: 1 });
  const other = span({ id: "other", name: "Release resources", kind: "resources.release" });
  call.children = [check];
  return testTrace([call, check, other]);
}

// A search that hides most of the list says how many operations matched, so the reader knows what they see.
describe("TimelineView search", () => {
  it("counts the operations that match, not the ancestors kept for context", async () => {
    const { host, unmount } = mount(h(TimelineView, { test: failingTree(), onSelect: () => {} }));
    setSearch(host, "assert");
    await nextTick();

    expect(host.querySelector(".count")?.textContent).toBe("1 of 3 matches");
    expect(names(host)).toEqual(["Create project", "Assert status"]);
    unmount();
  });

  it("stays quiet when nothing is searched for", async () => {
    const { host, unmount } = mount(h(TimelineView, { test: testTrace([span({})]), onSelect: () => {} }));
    await nextTick();

    expect(host.querySelector(".count")).toBeNull();
    unmount();
  });

  it("focuses the search on /", async () => {
    const { host, unmount } = mount(h(TimelineView, { test: testTrace([span({})]), onSelect: () => {} }));
    await nextTick();

    host.querySelector(".list")!.dispatchEvent(new KeyboardEvent("keydown", { key: "/", bubbles: true }));
    await nextTick();
    expect(document.activeElement).toBe(host.querySelector("input[aria-label='Find an operation']"));
    unmount();
  });
});

// The search answers where an operation is; the filter answers what needs attention, in the run's words.
describe("TimelineView attention filter", () => {
  it("shows only what needs attention, with its ancestors, and combines with the search", async () => {
    const { host, unmount } = mount(h(TimelineView, { test: failingTree(), onSelect: () => {} }));
    chip(host, "Needs attention").click();
    await nextTick();

    expect(host.querySelector(".count")?.textContent).toBe("1 of 3 need attention");
    expect(names(host)).toEqual(["Create project", "Assert status"]);

    setSearch(host, "release");
    await nextTick();
    expect(host.querySelector(".empty p")?.textContent).toBe("No flagged operation matches this search.");
    chip(host, "Show all operations").click();
    await nextTick();
    expect(names(host)).toHaveLength(3);
    unmount();
  });

  it("says when nothing needs attention, and when the test recorded nothing", async () => {
    const quiet = mount(h(TimelineView, { test: testTrace([span({})]), onSelect: () => {} }));
    chip(quiet.host, "Needs attention").click();
    await nextTick();
    expect(quiet.host.querySelector(".empty p")?.textContent).toBe("No operation needs attention.");
    quiet.unmount();

    const empty = mount(h(TimelineView, { test: testTrace([]), onSelect: () => {} }));
    await nextTick();
    expect(empty.host.querySelector(".empty p")?.textContent).toBe("This test recorded no operations.");
    empty.unmount();
  });
});

// The clock: each phase can be read on its own, framework machinery can step back, and untraced time is a row.
describe("TimelineView clock", () => {
  function phased(): TestTrace {
    const setup = span({ id: "setup", name: "Setup", kind: "test.setup", phase: "setup", start: 0, duration: 10, end: 10 });
    const hook = span({ id: "hook", name: "Before · Clients", kind: "hook.before", phase: "setup", start: 0, duration: 10, end: 10, parent: setup, depth: 1 });
    setup.children = [hook];
    const execution = span({ id: "exec", name: "Test execution", kind: "test.execution", start: 10, duration: 1000, end: 1010 });
    const call = span({
      id: "call", name: "REST · GET /org", start: 900, duration: 10, end: 910, parent: execution, depth: 1,
      moments: [{ at: 905, name: "Header set", kind: "http.header.configure", source: "ProtoTest.Rest", outcome: "succeeded", error: null, attributes: {}, sections: [], span: null }]
    });
    execution.children = [call];
    return testTrace([setup, hook, execution, call], 1010);
  }

  it("states untraced time as a row before the operation that ended it", async () => {
    const { host, unmount } = mount(h(TimelineView, { test: phased(), onSelect: () => {} }));
    await nextTick();

    const rows = [...host.querySelectorAll(".row")].map(row => row.classList.contains("gap") ? `gap ${row.querySelector(".gap-name")?.textContent}` : row.querySelector(".name")?.textContent);
    expect(rows).toEqual(["Setup", "Before · Clients", "Test execution", "gap 890 ms with no recorded operation", "REST · GET /org"]);
    expect(host.querySelectorAll(".row .moment")).toHaveLength(1);
    unmount();
  });

  it("zooms to one phase, with the ruler counting from its start", async () => {
    const { host, unmount } = mount(h(TimelineView, { test: phased(), onSelect: () => {} }));
    chip(host, "Execution").click();
    await nextTick();

    expect(names(host)).toEqual(["Test execution", "REST · GET /org"]);
    expect(host.querySelector(".head")?.textContent).toContain("2 of 4 operations, from +10 ms");
    expect(host.querySelector(".ruler .tick")?.textContent).toBe("0");
    unmount();
  });

  it("dims framework machinery, or hides it on request", async () => {
    const { host, unmount } = mount(h(TimelineView, { test: phased(), onSelect: () => {} }));
    await nextTick();
    expect([...host.querySelectorAll(".row.dim .name")].map(entry => entry.textContent)).toEqual(["Before · Clients"]);

    chip(host, "Hide").click();
    await nextTick();
    expect(names(host)).not.toContain("Before · Clients");
    unmount();
  });
});

// An operation's leavings read in the model's own words.
describe("TimelineView evidence marks", () => {
  it("names observations, attachments and moments", async () => {
    const call = span({
      id: "call", name: "Create project",
      evidence: [
        { type: "observation", at: 1, target: "projects", kind: "row-count", identifier: null, data: null, metadata: {}, span: null },
        { type: "attachment", at: 2, name: "shot.png", artifact: null, span: null }
      ],
      moments: [{ at: 3, name: "Server started", kind: "server.started", source: "ProtoTest", outcome: "succeeded", error: null, attributes: {}, sections: [], span: null }]
    });
    const { host, unmount } = mount(h(TimelineView, { test: testTrace([call]), onSelect: () => {} }));
    await nextTick();

    expect(host.querySelector(".row .events")?.textContent).toBe("1 observation, 1 attachment, 1 moment");
    expect(host.querySelectorAll(".row .evidence")).toHaveLength(2);
    unmount();
  });
});
