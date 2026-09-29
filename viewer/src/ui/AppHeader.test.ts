import { describe, expect, it } from "vitest";
import { createApp } from "vue";
import AppHeader from "./AppHeader.vue";

// The header carries the only path from a trace opened out of a CI artifact or a shared link back to the docs.
describe("AppHeader", () => {
  it("links to the docs site", () => {
    const host = document.createElement("div");
    document.body.append(host);
    const app = createApp(AppHeader);
    app.mount(host);

    const link = host.querySelector<HTMLAnchorElement>("a.docs");
    expect(link?.getAttribute("href")).toBe("https://prototest.dev/docs/");
    expect(link?.textContent?.trim()).toBe("Docs");

    app.unmount();
    host.remove();
  });
});
