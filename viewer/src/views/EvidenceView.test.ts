import { describe, expect, it } from "vitest";
import { createApp, h, nextTick, reactive } from "vue";
import EvidenceView from "./EvidenceView.vue";
import type { Artifact, Span, TestTrace } from "../trace/model";

function fixture(): TestTrace {
  const call: Span = {
    id: "call", parent: null, children: [], depth: 0, name: "Download workbook", kind: "http.request", source: "ProtoTest.Rest",
    phase: "execution", status: "succeeded", error: null, start: 100, duration: 100, end: 200, count: 1,
    attributes: {}, sections: [], moments: [], evidence: [], itemKey: null, item: null, changes: [], test: null
  };
  const artifact: Artifact = { id: "file", name: "report.xlsx", mediaType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", description: null, archivePath: "report.xlsx", sizeBytes: 100, error: null };
  call.evidence = [
    { type: "attachment", at: 150, name: "report.xlsx", artifact, span: call },
    { type: "observation", at: 120, target: "workbook", kind: "rows", identifier: "report", data: '{"count":3}', metadata: { source: "sheet" }, span: call }
  ];
  call.moments = [{ at: 140, name: "Response received", kind: "http.response", source: "ProtoTest.Rest", outcome: "succeeded", error: null, attributes: { status: "200" }, sections: [{ kind: "fields", label: "Headers", items: [{ label: "content-type", value: "xlsx", tone: "neutral", detail: null }], content: null, language: null }], span: call }];
  return {
    number: 1, id: "test", name: "Suite.Test", className: "Suite", method: "Test", outcome: "succeeded",
    duration: 100, start: 100, end: 200, spans: [call], roots: [call], byId: new Map([[call.id, call]]),
    moments: [{ at: 110, name: "Test started", kind: "test.started", source: "ProtoTest", outcome: "succeeded", error: null, attributes: {}, sections: [], span: null }],
    evidence: [{ type: "finding", at: 160, message: "Check export", status: "warning", category: "export", target: "report", tags: ["review"], metadata: { owner: "billing" }, span: null }],
    artifacts: new Map([[artifact.id, artifact]]), items: [], failure: null
  };
}

function mount(test: TestTrace) {
  const selected: Span[] = [];
  const opened: Artifact[] = [];
  const props = reactive({ test });
  const host = document.createElement("div");
  document.body.append(host);
  const app = createApp({ render: () => h(EvidenceView, { test: props.test, onSelect: (span: Span) => selected.push(span), onArtifact: (artifact: Artifact) => opened.push(artifact) }) });
  app.mount(host);
  return { host, props, selected, opened, unmount: () => { app.unmount(); host.remove(); } };
}

function click(host: HTMLElement, selector: string) {
  host.querySelector<HTMLButtonElement>(selector)!.click();
}

describe("Evidence", () => {
  it("orders all four kinds on the test clock and opens the recording operation or file", async () => {
    const { host, selected, opened, unmount } = mount(fixture());
    expect([...host.querySelectorAll(".what strong")].map(node => node.textContent)).toEqual(["Test started", "rows, report", "Response received", "report.xlsx", "Check export"]);
    expect([...host.querySelectorAll(".offset")].map(node => node.textContent)).toEqual(["+10 ms", "+20 ms", "+40 ms", "+50 ms", "+60 ms"]);
    expect(host.querySelectorAll(".from.none")).toHaveLength(2);
    click(host, "button.from");
    click(host, "button.open");
    await nextTick();
    expect(selected[0].id).toBe("call");
    expect(opened[0].name).toBe("report.xlsx");
    unmount();
  });

  it("expands observation metadata, finding details and moment attributes and sections", async () => {
    const { host, unmount } = mount(fixture());
    for (const button of host.querySelectorAll<HTMLButtonElement>("button.what")) button.click();
    await nextTick();
    for (const fact of ["count", "source", "sheet", "category", "export", "target", "report", "tags", "review", "owner", "billing", "status", "200", "Headers", "content-type", "xlsx"]) expect(host.textContent).toContain(fact);
    expect(host.querySelectorAll('button.what[aria-expanded="true"]')).toHaveLength(3);
    unmount();
  });

  it("filters by kind and resets the filter and expansion when another test opens", async () => {
    const { host, props, unmount } = mount(fixture());
    [...host.querySelectorAll<HTMLButtonElement>("button.chip")].find(button => button.textContent?.startsWith("Observations"))!.click();
    await nextTick();
    expect(host.querySelectorAll(".entry")).toHaveLength(1);
    click(host, "button.what");
    await nextTick();
    expect(host.querySelector(".body")).not.toBeNull();
    props.test = { ...fixture(), id: "another" };
    await nextTick();
    expect(host.querySelectorAll(".entry")).toHaveLength(5);
    expect(host.querySelector(".body")).toBeNull();
    unmount();
  });

  it("keeps unavailable and missing attachments honest", () => {
    const test = fixture();
    const file = test.spans[0].evidence[0];
    if (file.type !== "attachment" || !file.artifact) throw new Error("Missing fixture attachment");
    file.artifact.error = "Missing archive entry";
    test.evidence.push({ type: "attachment", at: 170, name: "missing.png", artifact: null, span: null });
    const { host, opened, unmount } = mount(test);
    expect(host.querySelector<HTMLButtonElement>("button.open")?.disabled).toBe(true);
    expect(host.textContent).toContain("Declared with no file");
    click(host, "button.open");
    expect(opened).toHaveLength(0);
    unmount();
  });

  it("explains an empty test", () => {
    const test = fixture();
    test.spans = []; test.evidence = []; test.moments = [];
    const { host, unmount } = mount(test);
    expect(host.textContent).toContain("This test recorded no observations, files, findings or moments.");
    unmount();
  });
});
