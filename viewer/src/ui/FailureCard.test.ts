import { describe, expect, it, vi } from "vitest";
import { createApp, h, type VNode } from "vue";
import FailureCard from "./FailureCard.vue";
import type { Failure, Span, TestTrace } from "../trace/model";

function span(overrides: Partial<Span>): Span {
  return {
    id: "check", parent: null, children: [], depth: 1, name: "Assert status", kind: "assert.http.status",
    source: "ProtoTest.Rest", phase: "execution", status: "failed",
    error: { type: "Error", message: "Expected 200, got 404" },
    start: 120, duration: 2, end: 122, count: 1, attributes: {}, sections: [], moments: [],
    evidence: [], itemKey: null, item: null, changes: [], test: null, ...overrides
  };
}

function testTrace(): TestTrace {
  return {
    number: 1, id: "t1", name: "Suite.Test", className: "Suite", method: "Test", outcome: "failed",
    duration: 200, start: 20, end: 220, spans: [], roots: [], byId: new Map(),
    moments: [], evidence: [], artifacts: new Map(), items: [], failure: null
  };
}

function failure(check: Span, call: Span | null = null): Failure {
  return { span: check, check: null, mismatches: [], call };
}

function mount(view: VNode) {
  const host = document.createElement("div");
  document.body.append(host);
  const app = createApp({ render: () => view });
  app.mount(host);
  return { host, unmount: () => { app.unmount(); host.remove(); } };
}

// The card answers why before anything else, but a why without a where sends the reader hunting.
describe("FailureCard where", () => {
  it("says which phase ran the failing check and how far into the test it started", () => {
    const check = span({});
    const { host, unmount } = mount(h(FailureCard, {
      failure: failure(check), outcome: "failed", test: testTrace(), onSelect: () => {}
    }));

    expect(host.querySelector(".where")?.textContent).toBe("Execution · +100 ms into the test");
    unmount();
  });

  it("opens the call the check judged when there is one", () => {
    const call = span({ id: "call", name: "Create project", kind: "http.request", status: "succeeded", error: null });
    const check = span({});
    const select = vi.fn();
    const { host, unmount } = mount(h(FailureCard, {
      failure: failure(check, call), outcome: "failed", test: testTrace(), onSelect: select
    }));

    const link = host.querySelector<HTMLButtonElement>(".call");
    expect(link?.textContent).toContain("Create project");
    link!.click();
    expect(select).toHaveBeenCalledWith(call);
    unmount();
  });
});
