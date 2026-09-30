import { describe, expect, it } from "vitest";
import { createApp, h } from "vue";
import SectionView from "./SectionView.vue";

describe("SectionView content", () => {
  it.each(["code", "custom"])("marks a binary %s payload rather than displaying broken glyphs", kind => {
    const host = document.createElement("div");
    const app = createApp({ render: () => h(SectionView, { section: { kind, label: "Body", content: "PK\uFFFD\u0000", language: "json", items: [] } }) });
    app.mount(host);
    expect(host.textContent).toContain("Binary content, recorded as 4 characters of text.");
    expect(host.textContent).not.toContain("\uFFFD");
    expect(host.querySelector("pre")).toBeNull();
    app.unmount();
  });

  it("leaves readable text intact", () => {
    const host = document.createElement("div");
    const app = createApp({ render: () => h(SectionView, { section: { kind: "code", label: "Body", content: "hello world", language: "text", items: [] } }) });
    app.mount(host);
    expect(host.querySelector("pre")?.textContent).toBe("hello world");
    expect(host.querySelector(".binary")).toBeNull();
    app.unmount();
  });
});
