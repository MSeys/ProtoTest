import { describe, expect, it, vi } from "vitest";
import { createApp, h, nextTick, type VNode } from "vue";
import SpanInspector from "./SpanInspector.vue";
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

function headings(host: HTMLElement): string[] {
  return [...host.querySelectorAll("h3")].map(entry => entry.textContent?.trim() ?? "");
}

// A short raw list reads inline; a long operation's list folds so the comparison above it stays on screen.
describe("SpanInspector attributes", () => {
  it("opens a short attribute list inline", async () => {
    const call = span({ attributes: { "http.method": "POST", "http.route": "/api/v1/projects" } });
    const { host, unmount } = mount(h(SpanInspector, { span: call, test: testTrace([call]) }));
    await nextTick();

    const details = host.querySelector("details.attributes");
    expect(details?.hasAttribute("open")).toBe(true);
    expect(details?.querySelector("summary")?.textContent?.replace(/\s+/g, " ")).toContain("Attributes 2");
    unmount();
  });

  it("folds an attribute list past a few groups", async () => {
    const call = span({ attributes: {
      "http.method": "POST", "auth.outcome": "applied", "client.name": "Northstar",
      "step.name": "Create", "flow.id": "7"
    } });
    const { host, unmount } = mount(h(SpanInspector, { span: call, test: testTrace([call]) }));
    await nextTick();

    const details = host.querySelector("details.attributes");
    expect(details?.hasAttribute("open")).toBe(false);
    expect(details?.querySelector("summary")?.textContent?.replace(/\s+/g, " ")).toContain("Attributes 5");
    unmount();
  });
});

// A shape comparison names itself once: the validated document's own head, not a second title above it.
describe("SpanInspector shape comparison", () => {
  it("keeps the validated document's head and no block title above it", async () => {
    const check = span({
      kind: "assert.json.shape", name: "Assert response shape", status: "failed",
      attributes: {
        "shape.result": "mismatched",
        "shape.matches": '["$.code"]',
        "shape.expected": '{"code":"forbidden"}',
        "shape.actual": '{"code":"forbidden","message":"No."}',
        "shape.mismatches": '[{"propertyPath":"$.message","reason":"was not expected","expected":"missing","actual":"No."}]'
      }
    });
    const { host, unmount } = mount(h(SpanInspector, { span: check, test: testTrace([check]) }));
    await nextTick();

    expect(headings(host)).not.toContain("Expected against actual");
    expect(host.querySelector(".card header strong")?.textContent).toBe("Validated document");
    unmount();
  });
});

// Every check on a call keeps its outcome dot and opens the check itself.
describe("SpanInspector checks", () => {
  it("marks each check with its outcome and selects it on click", async () => {
    const call = span({ id: "call", name: "Create project" });
    const check = span({ id: "check", name: "Assert status", kind: "assert.http.status", status: "failed", parent: call, depth: 1 });
    call.children = [check];
    const selected: Span[] = [];
    const { host, unmount } = mount(h(SpanInspector, {
      span: call, test: testTrace([call, check]), onSelect: (entry: Span) => selected.push(entry)
    }));
    await nextTick();

    const row = host.querySelector<HTMLButtonElement>(".block .link-row.danger");
    expect(row?.querySelector(".status.danger")).toBeTruthy();
    expect(row?.textContent).toContain("Assert status");
    row!.dispatchEvent(new MouseEvent("click", { bubbles: true }));
    await nextTick();

    expect(selected).toEqual([check]);
    unmount();
  });
});

// A moment that went wrong reads as such; an informational one stays quiet.
describe("SpanInspector moments", () => {
  it("shows moment attributes, errors and sections beside their time", async () => {
    const call = span({ moments: [{ at: 2, name: "Received", kind: "message.received", source: "Broker", outcome: "failed", error: { type: "Error", message: "Rejected" }, attributes: { queue: "invoices", missing: null }, sections: [{ label: "Payload", kind: "code", language: "text", content: "invoice", items: [] }], span: null }] });
    const { host, unmount } = mount(h(SpanInspector, { span: call, test: testTrace([call]) }));
    await nextTick();
    for (const text of ["Rejected", "queue", "invoices", "missing", "null", "Payload", "invoice"]) expect(host.querySelector(".moment-detail")?.textContent).toContain(text);
    unmount();
  });

  it("marks a failed moment and leaves a quiet one alone", async () => {
    const call = span({
      moments: [
        { at: 1, name: "Broker refused to close", kind: "resource.released", source: "ProtoTest", outcome: "failed", error: null, attributes: {}, sections: [], span: null },
        { at: 2, name: "Server started", kind: "server.started", source: "ProtoTest", outcome: "succeeded", error: null, attributes: {}, sections: [], span: null }
      ]
    });
    const { host, unmount } = mount(h(SpanInspector, { span: call, test: testTrace([call]) }));
    await nextTick();

    const rows = [...host.querySelectorAll(".moment")];
    expect(rows).toHaveLength(2);
    expect(rows[0].className).toContain("danger");
    expect(rows[1].className).not.toContain("danger");
    unmount();
  });
});

describe("SpanInspector evidence and index", () => {
  it("shows observation metadata and every finding detail", () => {
    const call = span({ evidence: [
      { type: "observation", at: 1, target: "orders", kind: "count", identifier: null, data: null, metadata: { origin: "database", absent: null }, span: null },
      { type: "finding", at: 2, message: "Slow export", status: "warning", category: "performance", target: "export", tags: ["slow", "review"], metadata: { elapsed: "500" }, span: null }
    ] });
    const { host, unmount } = mount(h(SpanInspector, { span: call, test: testTrace([call]) }));
    for (const text of ["origin", "database", "absent", "null", "category", "performance", "target", "export", "tags", "slow, review", "elapsed", "500"]) expect(host.querySelector('[data-block="evidence"]')?.textContent).toContain(text);
    unmount();
  });

  it("indexes sections and opens folded attributes before jumping to them", async () => {
    const call = span({
      sections: [{ label: "Request", kind: "code", language: "text", content: "hello", items: [] }],
      moments: [{ at: 1, name: "Sent", kind: "http.sent", source: "REST", outcome: "succeeded", error: null, attributes: {}, sections: [], span: null }],
      attributes: { "a.one": "1", "b.two": "2", "c.three": "3", "d.four": "4" }
    });
    const { host, unmount } = mount(h(SpanInspector, { span: call, test: testTrace([call]) }));
    const buttons = [...host.querySelectorAll<HTMLButtonElement>("nav.index button")];
    expect(buttons.map(button => button.textContent)).toEqual(["Request", "Moments", "Attributes"]);
    const attributes = host.querySelector<HTMLDetailsElement>('details[data-block="attributes"]')!;
    const scroll = vi.fn();
    attributes.scrollIntoView = scroll;
    expect(attributes.open).toBe(false);
    buttons[2].click();
    await nextTick();
    expect(attributes.open).toBe(true);
    expect(scroll).toHaveBeenCalledWith({ block: "start", behavior: "smooth" });
    const section = host.querySelector<HTMLElement>('[data-block="section-0"]')!;
    section.scrollIntoView = scroll;
    buttons[0].click();
    expect(scroll).toHaveBeenCalledTimes(2);
    unmount();
  });
});

// A file the operation attached opens like every other row in the details; one it could not store says so.
describe("SpanInspector attachments", () => {
  const artifact = { id: "a1", name: "body.json", mediaType: "application/json", sizeBytes: 21, archivePath: "artifacts/a1", description: null, error: null };

  it("opens an attached file from its row", async () => {
    const opened = vi.fn();
    const call = span({ evidence: [{ type: "attachment", at: 1, name: "body.json", artifact, span: null }] as Span["evidence"] });
    const { host, unmount } = mount(h(SpanInspector, { span: call, test: testTrace([call]), onArtifact: opened }));
    await nextTick();

    const row = host.querySelector<HTMLButtonElement>("button.link-row.file");
    expect([...row!.children].map(part => part.textContent)).toEqual(["File", "body.json", "application/json, 21 B"]);
    row!.click();
    expect(opened).toHaveBeenCalledWith(artifact);
    unmount();
  });

  it("disables the row of a file that could not be stored", async () => {
    const call = span({ evidence: [{ type: "attachment", at: 1, name: "shot.png", artifact: { ...artifact, error: "Not in the archive" }, span: null }] as Span["evidence"] });
    const { host, unmount } = mount(h(SpanInspector, { span: call, test: testTrace([call]) }));
    await nextTick();

    const row = host.querySelector<HTMLButtonElement>("button.link-row.file");
    expect(row?.disabled).toBe(true);
    expect(row?.textContent).toContain("Unavailable");
    unmount();
  });
});
