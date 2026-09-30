import { beforeEach, describe, expect, it } from "vitest";
import { createApp, h, nextTick, type VNode } from "vue";
import StepsView from "./StepsView.vue";
import type { Span, TestTrace } from "../trace/model";
import { frameworkMode } from "../ui/frameworkMode";

function span(overrides: Partial<Span>): Span {
  return {
    id: "span", parent: null, children: [], depth: 0, name: "span", kind: "http.request", source: "ProtoTest.Rest",
    phase: "execution", status: "succeeded", error: null, start: 0, duration: 1, end: 1, count: 1,
    attributes: {}, sections: [], moments: [], evidence: [], itemKey: null, item: null, changes: [], test: null,
    ...overrides
  };
}

function nest(parent: Span, children: Span[]): Span {
  parent.children = children;
  children.forEach(child => { child.parent = parent; child.depth = parent.depth + 1; });
  return parent;
}

function testTrace(roots: Span[], failure: TestTrace["failure"] = null): TestTrace {
  const all: Span[] = [];
  const walk = (list: Span[]) => list.forEach(entry => { all.push(entry); walk(entry.children); });
  walk(roots);
  return {
    number: 1, id: "t1", name: "Suite.Test", className: "Suite", method: "Test", outcome: failure ? "failed" : "succeeded",
    duration: 100, start: 0, end: 100, spans: all, roots, byId: new Map(all.map(entry => [entry.id, entry])),
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

beforeEach(() => {
  Element.prototype.scrollIntoView = function () {};
});

function phases(host: HTMLElement) {
  return [...host.querySelectorAll<HTMLElement>(".phase")].map(phase => ({
    name: phase.querySelector(".phase-head strong")?.textContent,
    open: phase.classList.contains("open"),
    summary: phase.querySelector(".summary")?.textContent
  }));
}

// The body is what the reader came for: setup and teardown fold to one line that says what they did.
describe("StepsView phases", () => {
  function passing() {
    const setup = nest(span({ id: "setup", name: "Setup", kind: "test.setup", phase: "setup", duration: 10, end: 10 }), [
      span({ id: "hook", name: "Before · Clients", kind: "hook.before", phase: "setup" })
    ]);
    nest(setup.children[0], [
      span({ id: "c1", name: "Initialize · Rest", kind: "client.initialize", phase: "setup" }),
      span({ id: "c2", name: "Initialize · GraphQL", kind: "client.initialize", phase: "setup" })
    ]);
    const execution = nest(span({ id: "exec", name: "Test execution", kind: "test.execution", start: 10, duration: 80, end: 90 }), [
      span({ id: "call", name: "REST · GET /projects", start: 12, end: 13 })
    ]);
    const teardown = span({ id: "teardown", name: "Teardown", kind: "test.teardown", phase: "teardown", start: 90, duration: 10, end: 100 });
    return testTrace([setup, execution, teardown]);
  }

  it("opens the body and folds setup and teardown into their summaries", async () => {
    const { host, unmount } = mount(h(StepsView, { test: passing(), onSelect: () => {} }));
    await nextTick();

    expect(phases(host)).toEqual([
      { name: "Setup", open: false, summary: "3 operations, 2 clients initialized" },
      { name: "Execution", open: true, summary: "1 operation, REST · GET /projects (1.0 ms)" },
      { name: "Teardown", open: false, summary: "0 operations" }
    ]);
    unmount();
  });

  it("opens a folded phase on request", async () => {
    const { host, unmount } = mount(h(StepsView, { test: passing(), onSelect: () => {} }));
    await nextTick();

    host.querySelector<HTMLButtonElement>(".phase .phase-head")!.click();
    await nextTick();
    expect(phases(host)[0].open).toBe(true);
    unmount();
  });

  // A failure never hides: the phase it happened in opens by itself, down to the failing row.
  it("opens a failing setup down to the failure", async () => {
    const failing = span({ id: "provision", name: "Provision tenant", kind: "data.provision", phase: "setup", status: "failed", error: { type: "Error", message: "Tenant refused" } });
    const setup = nest(span({ id: "setup", name: "Setup", kind: "test.setup", phase: "setup", status: "failed", duration: 10, end: 10 }), [
      nest(span({ id: "attr", name: "Before · Tenant", kind: "attribute.before", phase: "setup", status: "failed" }), [failing])
    ]);
    const test = testTrace([setup], { span: failing, check: null, mismatches: [], call: null });
    const { host, unmount } = mount(h(StepsView, { test, onSelect: () => {} }));
    await nextTick();

    expect(phases(host)[0].open).toBe(true);
    expect(host.querySelector(".line.danger .title")?.textContent).toBeTruthy();
    expect([...host.querySelectorAll(".title")].map(entry => entry.textContent)).toContain("Provision tenant");
    unmount();
  });
});

// The framework's own operations step back by default, and leave on request; a failing one always stays.
describe("StepsView framework", () => {
  function withHooks() {
    const execution = nest(span({ id: "exec", name: "Test execution", kind: "test.execution", duration: 80, end: 90 }), [
      span({ id: "h1", name: "Before · Clients", kind: "hook.before" }),
      span({ id: "h2", name: "Before · Data", kind: "hook.before" }),
      span({ id: "call", name: "REST · GET /projects" }),
      span({ id: "bad", name: "Initialize · Rest", kind: "client.initialize", status: "failed", error: { type: "Error", message: "No address" } })
    ]);
    return testTrace([execution]);
  }
  const titles = (host: HTMLElement) => [...host.querySelectorAll(".rows .line .title")].map(entry => entry.textContent);

  it("dims the framework group and keeps a failing framework step at full strength", async () => {
    frameworkMode.value = "dim";
    const { host, unmount } = mount(h(StepsView, { test: withHooks(), onSelect: () => {} }));
    await nextTick();

    expect([...host.querySelectorAll(".line.dim .title")].map(entry => entry.textContent)).toEqual(["2 extensions"]);
    expect(host.querySelector(".line.danger")?.classList.contains("dim")).toBe(false);
    unmount();
  });

  it("leaves the framework out when hidden, but not what failed", async () => {
    frameworkMode.value = "hide";
    const { host, unmount } = mount(h(StepsView, { test: withHooks(), onSelect: () => {} }));
    await nextTick();

    expect(titles(host)).toEqual(["REST · GET /projects", "Initialize · Rest"]);
    frameworkMode.value = "dim";
    unmount();
  });
});
