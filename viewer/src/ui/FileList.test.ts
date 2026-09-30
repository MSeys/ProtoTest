import { describe, expect, it } from "vitest";
import { createApp, h, nextTick, type VNode } from "vue";
import FileList, { type FileEntry } from "./FileList.vue";
import type { Artifact } from "../trace/model";

function artifact(name: string): Artifact {
  return {
    id: name, name, mediaType: "application/json", description: null,
    archivePath: `files/${name}`, sizeBytes: 12, error: null
  };
}

function mount(view: VNode) {
  const host = document.createElement("div");
  document.body.append(host);
  const app = createApp({ render: () => view });
  app.mount(host);
  return { host, unmount: () => { app.unmount(); host.remove(); } };
}

// No files at all is a fact about the run; no match is a fact about the search. Each says its own.
describe("FileList empty states", () => {
  it("says nothing was attached when the list is empty, with no reset to offer", async () => {
    const { host, unmount } = mount(h(FileList, { entries: [] as FileEntry[], onOpen: () => {} }));
    await nextTick();

    expect(host.querySelector(".empty p")?.textContent).toBe("No files were attached.");
    expect(host.querySelector(".empty button")).toBeNull();
    unmount();
  });

  it("offers the way back only when a search filtered everything out", async () => {
    const entries = Array.from({ length: 9 }, (_, index) => ({ artifact: artifact(`file-${index}.json`), detail: null }));
    const { host, unmount } = mount(h(FileList, { entries, onOpen: () => {} }));
    await nextTick();

    const input = host.querySelector<HTMLInputElement>("input[aria-label='Find a file']");
    expect(input).toBeTruthy();
    input!.value = "nothing-matches-this";
    input!.dispatchEvent(new Event("input", { bubbles: true }));
    await nextTick();

    expect(host.querySelector(".empty p")?.textContent).toBe("No file matches this search.");
    const reset = host.querySelector<HTMLButtonElement>(".empty button");
    expect(reset?.textContent).toContain("Show all files");
    reset!.click();
    await nextTick();
    expect(host.querySelectorAll(".file-row")).toHaveLength(9);
    unmount();
  });
});
