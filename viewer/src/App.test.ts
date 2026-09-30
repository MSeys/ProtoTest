import { afterEach, describe, expect, it, vi } from "vitest";
import { readFile } from "node:fs/promises";
import { createApp, nextTick } from "vue";
import App from "./App.vue";
import { route } from "./router";

afterEach(() => { vi.unstubAllGlobals(); history.replaceState(null, "", "/"); route.value = { name: "run" }; });

describe("run inspector through the app", () => {
  it("opens run state, follows its operation and restores a run selection link", async () => {
    const archive = await readFile("public/demos/prototest-demo.prototrace");
    vi.stubGlobal("fetch", vi.fn(async () => ({ ok: true, arrayBuffer: async () => archive.buffer.slice(archive.byteOffset, archive.byteOffset + archive.byteLength) })));
    vi.stubGlobal("matchMedia", () => ({ matches: true, addEventListener: () => {} }));
    Object.defineProperty(HTMLElement.prototype, "scrollTo", { configurable: true, value: vi.fn() });
    history.replaceState(null, "", "/?demo=1#/run/operations");
    route.value = { name: "run", view: "operations" };
    const host = document.createElement("div"); document.body.append(host);
    const app = createApp(App); app.mount(host);
    try {
      await vi.waitFor(() => expect(host.querySelector(".run-items button")).not.toBeNull());
      host.querySelector<HTMLButtonElement>(".run-items button")!.click();
      await nextTick();
      expect(route.value).toMatchObject({ name: "run", selection: { item: expect.anything() } });
      expect(host.querySelector(".inspector")).not.toBeNull();
      expect(host.querySelector(".inspector .path-label")?.textContent).toBe("State of the run");
      host.querySelector<HTMLButtonElement>('[aria-label="Close details"]')!.click();
      await nextTick();
      expect(host.querySelector(".inspector")).toBeNull();
      const operation = host.querySelector<HTMLButtonElement>(".operation")!;
      operation.click();
      await nextTick();
      expect(route.value).toMatchObject({ name: "run", selection: { span: expect.any(String) } });
      expect(host.querySelector(".inspector .facts")?.textContent).toContain("into the run");
      const link = location.hash;
      host.querySelector<HTMLButtonElement>('[aria-label="Close details"]')!.click();
      location.hash = link;
      window.dispatchEvent(new Event("hashchange"));
      await nextTick();
      expect(host.querySelector(".inspector")).not.toBeNull();
    } finally { app.unmount(); host.remove(); Reflect.deleteProperty(HTMLElement.prototype, "scrollTo"); }
  });
});
