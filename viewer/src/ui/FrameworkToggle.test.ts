import { describe, expect, it } from "vitest";
import { createApp, nextTick } from "vue";
import FrameworkToggle from "./FrameworkToggle.vue";
import { frameworkMode } from "./frameworkMode";

// One switch for every view, remembered for the next visit.
describe("FrameworkToggle", () => {
  it("sets the shared mode and remembers it", async () => {
    frameworkMode.value = "dim";
    const host = document.createElement("div");
    const app = createApp(FrameworkToggle);
    app.mount(host);

    const button = (label: string) => [...host.querySelectorAll<HTMLButtonElement>("button")].find(entry => entry.textContent === label)!;
    expect(button("Dim").getAttribute("aria-checked")).toBe("true");
    button("Hide").click();
    await nextTick();
    expect(frameworkMode.value).toBe("hide");
    expect(button("Hide").getAttribute("aria-checked")).toBe("true");
    expect(localStorage.getItem("prototrace.framework")).toBe("hide");

    frameworkMode.value = "dim";
    app.unmount();
  });
});
