import { describe, expect, it } from "vitest";
import { createApp, h } from "vue";
import Panel from "./Panel.vue";

// A panel's explanation is there for whoever asks: on hover and to a screen reader, not as a standing line.
describe("Panel", () => {
  it("keeps the subtitle as the title's description", () => {
    const host = document.createElement("div");
    const app = createApp({ render: () => h(Panel, { title: "Run timeline", subtitle: "Tests in start order." }, () => "body") });
    app.mount(host);

    const title = host.querySelector("h2")!;
    const hint = host.querySelector(".hint")!;
    expect(title.getAttribute("title")).toBe("Tests in start order.");
    expect(title.getAttribute("aria-describedby")).toBe(hint.id);
    expect(hint.textContent).toBe("Tests in start order.");
    app.unmount();
  });
});
