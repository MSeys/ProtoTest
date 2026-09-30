import { describe, expect, it, vi } from "vitest";
import { createApp, h, type VNode } from "vue";
import VerdictBar from "./VerdictBar.vue";
import PhaseBand from "./PhaseBand.vue";
import type { Evidence, Failure, Span, TestTrace } from "../trace/model";

function span(overrides: Partial<Span>): Span {
  return {
    id: "check", parent: null, children: [], depth: 1, name: "Assert status", kind: "assert.http.status",
    source: "ProtoTest.Rest", phase: "execution", status: "failed",
    error: { type: "Error", message: "Expected 200, got 404\nResponse body: {}" },
    start: 120, duration: 2, end: 122, count: 1, attributes: {}, sections: [], moments: [],
    evidence: [], itemKey: null, item: null, changes: [], test: null, ...overrides
  };
}

function testTrace(overrides: Partial<TestTrace> = {}): TestTrace {
  return {
    number: 1, id: "t1", name: "Suite.Test", className: "Suite", method: "Test", outcome: "failed",
    duration: 200, start: 20, end: 220, spans: [], roots: [], byId: new Map(),
    moments: [], evidence: [], artifacts: new Map(), items: [], failure: null, ...overrides
  };
}

function failure(check: Span, call: Span | null = null, overrides: Partial<Failure> = {}): Failure {
  return { span: check, check: null, mismatches: [], call, ...overrides };
}

function mount(view: VNode) {
  const host = document.createElement("div");
  document.body.append(host);
  const app = createApp({ render: () => view });
  app.mount(host);
  return { host, unmount: () => { app.unmount(); host.remove(); } };
}

const text = (host: HTMLElement, selector: string) => host.querySelector(selector)?.textContent?.replace(/\s+/g, " ").trim();

// The verdict speaks the CLI's words, so a CI log line and the viewer name a failure the same way.
describe("VerdictBar", () => {
  it("names an assertion, where it happened and the first difference", () => {
    const check = span({ kind: "assert.json.shape", name: "Assert response shape" });
    const test = testTrace({
      failure: failure(check, span({ id: "call", name: "REST · GET /org", kind: "http.request", status: "succeeded", error: null }), {
        mismatches: [{ path: "$.status", reason: "", expected: "past_due", actual: "active" }, { path: "$.plan", reason: "", expected: "a", actual: "b" }]
      })
    });
    const { host, unmount } = mount(h(VerdictBar, { test }));

    expect(text(host, ".rule")).toBe("Assertion");
    expect(text(host, ".what")).toBe("Assert response shape");
    expect(text(host, ".call")).toBe("on REST · GET /org");
    expect(host.querySelector(".what")?.getAttribute("title")).toBe("Execution, +100 ms into the test");
    expect(text(host, ".detail")).toBe('$.status: expected "past_due", got "active", and 1 more');
    unmount();
  });

  it("names a runner failure by the first line of its error and opens the operation", () => {
    const execution = span({ id: "exec", name: "Test execution", kind: "test.execution", error: { type: "HttpRequestException", message: "ConnectionError: refused.\nat ..." } });
    const select = vi.fn();
    const { host, unmount } = mount(h(VerdictBar, { test: testTrace({ failure: failure(execution) }), onSelect: select }));

    expect(text(host, ".rule")).toBe("Runner failure");
    expect(text(host, ".detail")).toBe("ConnectionError: refused.");
    host.querySelector<HTMLButtonElement>("button.what")!.click();
    expect(select).toHaveBeenCalledWith(execution);
    unmount();
  });

  it("explains a partial test by its finding", () => {
    const finding: Evidence = {
      type: "finding", at: 153, message: "The response carried 4 fields no assertion mentioned.", status: "Warning",
      category: "Coverage", target: null, tags: [], metadata: {}, span: null
    };
    const { host, unmount } = mount(h(VerdictBar, { test: testTrace({ outcome: "partial", evidence: [finding] }) }));

    expect(text(host, ".rule")).toBe("Finding");
    expect(text(host, ".what")).toBe("The response carried 4 fields no assertion mentioned.");
    expect(host.querySelector(".what")?.getAttribute("title")).toBe("Recorded +133 ms into the test");
    expect(text(host, ".detail")).toBe("Warning, Coverage");
    unmount();
  });
});

// The band says in words what the hatching shows: how much of a phase ran with no operation.
describe("PhaseBand", () => {
  it("states each phase's length and its untraced time", () => {
    const setup = span({ id: "setup", name: "Setup", kind: "test.setup", phase: "setup", status: "succeeded", error: null, start: 20, duration: 30, end: 50 });
    const execution = span({ id: "exec", name: "Test execution", kind: "test.execution", status: "succeeded", error: null, start: 50, duration: 1000, end: 1050 });
    const client = span({ id: "client", kind: "client.initialize", phase: "setup", status: "succeeded", error: null, start: 20, duration: 30, end: 50, parent: setup });
    setup.children = [client];
    const { host, unmount } = mount(h(PhaseBand, { test: testTrace({ duration: 1030, roots: [setup, execution], spans: [setup, client, execution] }) }));

    expect([...host.querySelectorAll(".legend > span")].map(entry => entry.textContent?.replace(/\s+/g, " ").trim()))
      .toEqual(["Setup 30 ms", "Execution 1.00 s, 1.00 s without an operation"]);
    expect(host.querySelectorAll(".untraced")).toHaveLength(1);
    unmount();
  });
});
