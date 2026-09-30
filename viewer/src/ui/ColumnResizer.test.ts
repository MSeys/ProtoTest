import { describe, expect, it, vi } from "vitest";
import { createApp, h } from "vue";
import ColumnResizer from "./ColumnResizer.vue";

// A focusable separator invites the keyboard, so the arrow keys resize and Enter resets.
// It reports its position like the window-splitter pattern: a value always accompanies the label.
describe("ColumnResizer", () => {
  function mount(extra: Record<string, unknown> = {}) {
    const host = document.createElement("div");
    document.body.append(host);
    const nudge = vi.fn();
    const reset = vi.fn();
    const app = createApp({
      render: () => h(ColumnResizer, {
        label: "Resize the test list", min: 200, max: 460, edge: "leading",
        onNudge: nudge, onReset: reset, ...extra
      })
    });
    app.mount(host);
    return { host, app, nudge, reset };
  }

  it("nudges with the arrow keys and resets with Enter", () => {
    const { host, app, nudge, reset } = mount();

    const resizer = host.querySelector<HTMLElement>("[role='separator']")!;
    expect(resizer.tabIndex).toBe(0);
    expect(resizer.getAttribute("aria-label")).toBe("Resize the test list");

    resizer.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowRight", bubbles: true }));
    expect(nudge).toHaveBeenCalledTimes(1);
    expect(nudge.mock.calls[0][1]).toBe(16);

    resizer.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowLeft", bubbles: true }));
    expect(nudge.mock.calls[1][1]).toBe(-16);

    resizer.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", bubbles: true }));
    expect(reset).toHaveBeenCalledTimes(1);

    app.unmount();
    host.remove();
  });

  it("names its range and current width for assistive technology", () => {
    const { host, app } = mount({ now: 250 });
    const resizer = host.querySelector<HTMLElement>("[role='separator']")!;
    expect(resizer.getAttribute("aria-orientation")).toBe("vertical");
    expect(resizer.getAttribute("aria-valuemin")).toBe("200");
    expect(resizer.getAttribute("aria-valuemax")).toBe("460");
    expect(resizer.getAttribute("aria-valuenow")).toBe("250");
    expect(resizer.getAttribute("aria-valuetext")).toBe("250 pixels wide");
    app.unmount();
    host.remove();
  });

  it("reports a value even before a width was set", () => {
    const { host, app } = mount();
    expect(host.querySelector("[role='separator']")!.getAttribute("aria-valuenow")).toBeTruthy();
    app.unmount();
    host.remove();
  });
});
