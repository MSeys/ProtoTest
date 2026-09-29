import { describe, expect, it } from "vitest";
import { createApp, nextTick } from "vue";
import DemoList from "./DemoList.vue";
import type { DemoFacts } from "../demos";

// The demos are two groups: the product runs draw their own outcome shape, the recipes stay quiet.
describe("DemoList", () => {
  it("groups the runs and the recipes, and draws each run's own shape", async () => {
    const facts: Record<string, DemoFacts | null> = {
      full: { tests: 3, failed: 1, partial: 1, cancelled: 0, outcomes: ["failed", "partial", "succeeded"] },
      opencsms: { tests: 2, failed: 0, partial: 0, cancelled: 0, outcomes: ["succeeded", "succeeded"] },
      "rest-graphql": { tests: 1, failed: 0, partial: 0, cancelled: 0, outcomes: ["succeeded"] },
      "rest-database": null,
      workbook: null
    };
    const host = document.createElement("div");
    document.body.append(host);
    const app = createApp(DemoList, { facts, onOpen: () => {} });
    app.mount(host);
    await nextTick();

    const cards = host.querySelectorAll(".demo-card");
    expect(cards.length).toBe(2);
    expect(cards[0].querySelectorAll(".demo-strip i").length).toBe(3);
    expect(cards[0].querySelectorAll(".demo-strip i.danger").length).toBe(1);
    expect(cards[0].querySelectorAll(".demo-strip i.warning").length).toBe(1);
    expect(cards[1].querySelectorAll(".demo-strip i").length).toBe(2);
    expect(host.querySelectorAll(".demo-recipes .demo-row").length).toBe(3);
    // A demo whose facts failed to read stays listed and clickable, without a facts line.
    expect(host.querySelectorAll(".demo-row")[1].textContent).not.toContain("reading");

    app.unmount();
    host.remove();
  });

  it("shows the reading state until the facts arrive", async () => {
    const host = document.createElement("div");
    document.body.append(host);
    const app = createApp(DemoList, { facts: {}, onOpen: () => {} });
    app.mount(host);
    await nextTick();

    expect(host.querySelector(".demo-facts")?.textContent?.trim()).toBe("reading…");

    app.unmount();
    host.remove();
  });
});
