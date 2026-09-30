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

  // The path says where the reader is and is the way back up: the run, the test, what the details show.
  it("shows the path from the run down, with the last step current", () => {
    const host = document.createElement("div");
    document.body.append(host);
    const app = createApp(AppHeader, {
      trail: [
        { label: "demo.prototrace", href: "#/" },
        { label: "12 A real wait", href: "#/test/t12/story" },
        { label: "Assert response shape", href: "#/test/t12/story?span=39" }
      ]
    });
    app.mount(host);

    const links = [...host.querySelectorAll<HTMLAnchorElement>(".trail a")];
    expect(links.map(link => [link.textContent, link.getAttribute("href")])).toEqual([
      ["demo.prototrace", "#/"],
      ["12 A real wait", "#/test/t12/story"],
      ["Assert response shape", "#/test/t12/story?span=39"]
    ]);
    expect(links.map(link => link.getAttribute("aria-current"))).toEqual([null, null, "page"]);
    // The privacy line belongs to the start screen; with a trace open the path takes its place.
    expect(host.querySelector(".privacy")).toBeNull();

    app.unmount();
    host.remove();
  });

  it("states that the trace stays in the browser on the start screen", () => {
    const host = document.createElement("div");
    document.body.append(host);
    const app = createApp(AppHeader);
    app.mount(host);

    expect(host.querySelector(".privacy")?.textContent).toContain("Trace stays in this browser");
    expect(host.querySelector(".rail-toggle")).toBeNull();

    app.unmount();
    host.remove();
  });

  it("offers the test list toggle when a trace is open and says what it does", () => {
    const toggle = vi.fn();
    const host = document.createElement("div");
    document.body.append(host);
    const app = createApp(AppHeader, { rail: { open: true }, onToggleRail: toggle });
    app.mount(host);

    const button = host.querySelector<HTMLButtonElement>(".rail-toggle");
    expect(button?.getAttribute("aria-label")).toBe("Hide the test list");
    expect(button?.getAttribute("aria-expanded")).toBe("true");
    button!.click();
    expect(toggle).toHaveBeenCalledTimes(1);

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
