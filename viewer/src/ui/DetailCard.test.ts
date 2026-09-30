import { describe, expect, it } from "vitest";
import { createApp, h } from "vue";
import DetailCard from "./DetailCard.vue";

function mount(props: Record<string, unknown>, tools = false) {
  const host = document.createElement("div");
  const app = createApp({ render: () => h(DetailCard, props, { default: () => h("pre", "body"), ...(tools ? { tools: () => h("button", "Copy") } : {}) }) });
  app.mount(host);
  return { host, unmount: () => app.unmount() };
}

// Every recorded document in the details shares one head: its name, its size, its tools.
describe("DetailCard", () => {
  it("names the document, says its size and holds its tools", () => {
    const { host, unmount } = mount({ title: "FailureDrills.cs:36", meta: "Northstar.FailureDrills.Run", hint: "tests/FailureDrills.cs", mono: true }, true);

    expect(host.querySelector("header strong")?.textContent).toBe("FailureDrills.cs:36");
    expect(host.querySelector("header strong")?.getAttribute("title")).toBe("tests/FailureDrills.cs");
    expect(host.querySelector("header small")?.textContent).toBe("Northstar.FailureDrills.Run");
    expect(host.querySelector(".tools button")?.textContent).toBe("Copy");
    expect(host.querySelector("pre")?.textContent).toBe("body");
    unmount();
  });

  it("leaves out what it was not given", () => {
    const { host, unmount } = mount({ meta: "14 fields" });

    expect(host.querySelector("header strong")).toBeNull();
    expect(host.querySelector(".tools")).toBeNull();
    unmount();
  });
});
