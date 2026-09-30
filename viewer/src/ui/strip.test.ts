import { describe, expect, it } from "vitest";
import { createApp, h, nextTick, withDirectives } from "vue";
import { vStrip } from "./strip";

/** A strip whose content is wider than itself, since jsdom does no layout. */
function mount() {
  const host = document.createElement("div");
  document.body.append(host);
  const app = createApp({ render: () => withDirectives(h("div", { class: "strip" }, [h("button", "One"), h("button", "Two")]), [[vStrip]]) });
  app.mount(host);
  const strip = host.querySelector<HTMLElement>(".strip")!;
  Object.defineProperty(strip, "scrollWidth", { configurable: true, value: 400 });
  Object.defineProperty(strip, "clientWidth", { configurable: true, value: 100 });
  strip.dispatchEvent(new Event("scroll"));
  return { strip, unmount: () => { app.unmount(); host.remove(); } };
}

const wheel = (strip: HTMLElement, deltaY: number, deltaX = 0) => {
  const event = new WheelEvent("wheel", { deltaY, deltaX, cancelable: true });
  strip.dispatchEvent(event);
  return event;
};

// A strip scrolls with a plain mouse wheel while it has room, and says which sides have more.
describe("v-strip", () => {
  it("marks the strip and the side that has more", async () => {
    const { strip, unmount } = mount();
    await nextTick();
    expect(strip.dataset.strip).toBe("");
    expect(strip.dataset.more).toBe("end");
    unmount();
  });

  it("turns a vertical wheel into a sideways scroll, then hands it back at the end", () => {
    const { strip, unmount } = mount();

    const first = wheel(strip, 120);
    expect(first.defaultPrevented).toBe(true);
    expect(strip.scrollLeft).toBe(120);
    expect(strip.dataset.more).toBe("both");

    wheel(strip, 500);
    expect(strip.scrollLeft).toBe(300);
    expect(strip.dataset.more).toBe("start");
    expect(wheel(strip, 50).defaultPrevented).toBe(false);
    unmount();
  });

  it("leaves a sideways gesture to the browser", () => {
    const { strip, unmount } = mount();
    expect(wheel(strip, 10, 60).defaultPrevented).toBe(false);
    expect(strip.scrollLeft).toBe(0);
    unmount();
  });
});
