import { describe, expect, it } from "vitest";
import { createApp, h } from "vue";
import KindChip from "./KindChip.vue";

function render(quiet?: boolean) {
  const host = document.createElement("div");
  const app = createApp({ render: () => h(KindChip, { type: { id: "call", label: "Call" }, quiet }) });
  app.mount(host);
  const chip = host.querySelector(".chip");
  app.unmount();
  return chip;
}

// A list row names its kind quietly; the inspector's head keeps the filled chip.
describe("KindChip", () => {
  it("fills by default and drops the fill when quiet", () => {
    expect(render()?.classList.contains("quiet")).toBe(false);
    expect(render(true)?.classList.contains("quiet")).toBe(true);
    expect(render(true)?.textContent).toBe("Call");
  });
});
