import { describe, expect, it, vi } from "vitest";
import { createApp, h } from "vue";
import Tabs from "./Tabs.vue";

const items = [
  { id: "run", label: "Run", href: "#/" },
  { id: "story", label: "Story", href: "#/test/1/story" },
  { id: "files", label: "Files", href: "#/test/1/files" }
];

// The route strip is the viewer's tab list; the docs mocks follow the same pattern, so the two cannot drift.
describe("Tabs", () => {
  function mount(extra: Record<string, unknown> = {}) {
    const host = document.createElement("div");
    document.body.append(host);
    const select = vi.fn();
    const app = createApp({
      render: () => h(Tabs, { items, active: "run", label: "Views", panel: "workspace-view", onSelect: select, ...extra })
    });
    app.mount(host);
    return { host, app, select };
  }

  it("is a tab list whose tabs point at one panel with roving focus", () => {
    const { host, app } = mount();
    const list = host.querySelector<HTMLElement>("[role='tablist']");
    expect(list?.getAttribute("aria-label")).toBe("Views");
    const tabs = [...host.querySelectorAll<HTMLAnchorElement>("[role='tab']")];
    expect(tabs.map(tab => tab.id)).toEqual(["workspace-view-tab-run", "workspace-view-tab-story", "workspace-view-tab-files"]);
    expect(tabs.map(tab => tab.getAttribute("aria-controls"))).toEqual(["workspace-view", "workspace-view", "workspace-view"]);
    expect(tabs.map(tab => tab.tabIndex)).toEqual([0, -1, -1]);
    expect(tabs.map(tab => tab.getAttribute("aria-selected"))).toEqual(["true", "false", "false"]);
    app.unmount();
    host.remove();
  });

  it("moves focus and selection with the arrow, Home and End keys", () => {
    const { host, app, select } = mount();
    const list = host.querySelector<HTMLElement>("[role='tablist']")!;
    const tabs = [...host.querySelectorAll<HTMLElement>("[role='tab']")];

    list.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowRight", bubbles: true }));
    expect(select).toHaveBeenLastCalledWith("story");
    expect(document.activeElement).toBe(tabs[1]);

    list.dispatchEvent(new KeyboardEvent("keydown", { key: "End", bubbles: true }));
    expect(select).toHaveBeenLastCalledWith("files");
    expect(document.activeElement).toBe(tabs[2]);

    list.dispatchEvent(new KeyboardEvent("keydown", { key: "Home", bubbles: true }));
    expect(select).toHaveBeenLastCalledWith("run");
    expect(document.activeElement).toBe(tabs[0]);

    list.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowLeft", bubbles: true }));
    expect(select).toHaveBeenLastCalledWith("files");
    expect(document.activeElement).toBe(tabs[2]);

    app.unmount();
    host.remove();
  });

  it("reads a pill row as pressed filter buttons, not tabs", () => {
    const { host, app, select } = mount({
      items: [{ id: "all", label: "All" }, { id: "attention", label: "Needs attention" }],
      variant: "pill",
      active: "all"
    });
    expect(host.querySelector("[role='tablist']")).toBeNull();
    expect(host.querySelector("[role='group']")).not.toBeNull();
    const buttons = [...host.querySelectorAll<HTMLButtonElement>("button")];
    expect(buttons.map(button => button.getAttribute("aria-pressed"))).toEqual(["true", "false"]);
    buttons[1].click();
    expect(select).toHaveBeenCalledWith("attention");
    app.unmount();
    host.remove();
  });
});
