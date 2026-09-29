import { describe, expect, it, vi } from "vitest";
import { createApp, nextTick } from "vue";
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

  // The copy control shares the demo or trace URL behind the open trace; a local file has none.
  it("offers no copy control without a shareable source", () => {
    const host = document.createElement("div");
    document.body.append(host);
    const app = createApp(AppHeader);
    app.mount(host);

    expect([...host.querySelectorAll("button")].map(button => button.textContent?.trim()))
      .not.toContain("Copy link");

    app.unmount();
    host.remove();
  });

  it("copies the shared link and confirms", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    vi.stubGlobal("navigator", { clipboard: { writeText } });
    const host = document.createElement("div");
    document.body.append(host);
    const app = createApp(AppHeader, { share: "https://trace.prototest.dev/?demo=1" });
    app.mount(host);

    const button = [...host.querySelectorAll("button")].find(candidate => candidate.textContent?.trim() === "Copy link");
    expect(button).toBeTruthy();
    button!.click();
    await nextTick();
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(writeText).toHaveBeenCalledWith("https://trace.prototest.dev/?demo=1");
    expect(button!.textContent?.trim()).toBe("Copied");

    app.unmount();
    host.remove();
    vi.unstubAllGlobals();
  });
});
