import { describe, expect, it, vi } from "vitest";
import { createApp, h } from "vue";
import ColumnResizer from "./ColumnResizer.vue";

// A focusable separator invites the keyboard, so the arrow keys resize and Enter resets.
describe("ColumnResizer", () => {
  it("nudges with the arrow keys and resets with Enter", () => {
    const host = document.createElement("div");
    document.body.append(host);
    const nudge = vi.fn();
    const reset = vi.fn();
    const app = createApp({
      render: () => h(ColumnResizer, { label: "Resize the test list", onNudge: nudge, onReset: reset })
    });
    app.mount(host);

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
});
