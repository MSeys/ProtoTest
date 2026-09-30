import { describe, expect, it } from "vitest";
import { createApp, h } from "vue";
import PropertyList, { type Property } from "./PropertyList.vue";

function render(props: { entries: Property[]; mono?: boolean; inline?: boolean }) {
  const host = document.createElement("div");
  const app = createApp({ render: () => h(PropertyList, props) });
  app.mount(host);
  return { host, unmount: () => app.unmount() };
}

// Every key-value list in the details reads the same: one row per property, the full key a hover away.
describe("PropertyList", () => {
  it("renders one row per property with its label, tone, detail and null", () => {
    const { host, unmount } = render({ entries: [
      { key: "resource.state", label: "state", value: "released" },
      { key: "status", value: "200", tone: "success", detail: "OK" },
      { key: "owner", value: null }
    ] });

    const rows = [...host.querySelectorAll(".row")];
    expect(rows).toHaveLength(3);
    expect(rows[0].querySelector("dt")?.textContent).toBe("state");
    expect(rows[0].querySelector("dt")?.getAttribute("title")).toBe("resource.state");
    expect(rows[1].querySelector("dd")?.classList.contains("success")).toBe(true);
    expect(rows[1].querySelector("dd small")?.textContent).toBe("OK");
    expect(rows[2].querySelector("dd")?.classList.contains("null")).toBe(true);
    expect(rows[2].querySelector("dd")?.textContent?.trim()).toBe("null");
    unmount();
  });

  it("marks raw lists in the code face and inline lists without side padding", () => {
    const { host, unmount } = render({ entries: [{ key: "a", value: "b" }], mono: true, inline: true });
    expect(host.querySelector(".properties")?.classList.contains("mono")).toBe(true);
    expect(host.querySelector(".properties")?.classList.contains("inline")).toBe(true);
    unmount();
  });
});
