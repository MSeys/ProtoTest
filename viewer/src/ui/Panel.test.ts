import { describe, expect, it } from "vitest";
import { createApp, h } from "vue";
import Panel from "./Panel.vue";

// A panel's explanation is there for whoever asks: on hover and to a screen reader, not as a standing line.
describe("Panel", () => {
  it("keeps the subtitle behind a mark that describes it", () => {
    const host = document.createElement("div");
    const app = createApp({ render: () => h(Panel, { title: "Run timeline", subtitle: "Tests in start order." }, () => "body") });
    app.mount(host);

    const mark = host.querySelector("h2 .mark")!;
    const tip = host.querySelector("[role='tooltip']")!;
    expect(host.querySelector("h2")?.hasAttribute("title")).toBe(false);
    expect(mark.getAttribute("aria-describedby")).toBe(tip.id);
    expect(tip.textContent).toBe("Tests in start order.");
    app.unmount();
  });
});
