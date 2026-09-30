import type { Directive } from "vue";

/*
 * A strip that scrolls sideways - view tabs, a section index, sheet tabs - with no scrollbar of its own. A
 * mouse wheel scrolls it along while it has room, then hands the scroll back to the page; a fade marks each
 * side that still has more; the selected entry is brought into view when it changes.
 */
interface StripState {
  observer?: ResizeObserver;
  active?: Element | null;
  wheel: (event: WheelEvent) => void;
  update: () => void;
}

const states = new WeakMap<HTMLElement, StripState>();

/** Which sides still have more to show: "start", "end", "both" or nothing. */
export function stripMore(element: HTMLElement): string {
  const max = element.scrollWidth - element.clientWidth;
  if (max <= 1) return "";
  const start = element.scrollLeft > 1;
  const end = element.scrollLeft < max - 1;
  return start && end ? "both" : start ? "start" : end ? "end" : "";
}

function selected(element: HTMLElement): Element | null {
  return element.querySelector("[aria-selected='true'], [aria-pressed='true'], .active");
}

function reveal(element: HTMLElement, state: StripState) {
  const active = selected(element);
  // Only the strip moves: scrolling the entry into view could also scroll the page.
  if (active instanceof HTMLElement && active !== state.active) {
    const left = active.offsetLeft - element.offsetLeft;
    if (left < element.scrollLeft) element.scrollLeft = left;
    else if (left + active.offsetWidth > element.scrollLeft + element.clientWidth) element.scrollLeft = left + active.offsetWidth - element.clientWidth;
  }
  state.active = active;
}

export const vStrip: Directive<HTMLElement> = {
  mounted(element) {
    const update = () => {
      const more = stripMore(element);
      if (more) element.dataset.more = more; else delete element.dataset.more;
    };
    const wheel = (event: WheelEvent) => {
      if (Math.abs(event.deltaX) >= Math.abs(event.deltaY)) return;
      const max = element.scrollWidth - element.clientWidth;
      if (max <= 0) return;
      const next = Math.max(0, Math.min(max, element.scrollLeft + event.deltaY));
      if (next === element.scrollLeft) return;
      event.preventDefault();
      element.scrollLeft = next;
      update();
    };
    const state: StripState = { wheel, update };
    element.dataset.strip = "";
    element.addEventListener("wheel", wheel, { passive: false });
    element.addEventListener("scroll", update, { passive: true });
    if (typeof ResizeObserver !== "undefined") {
      state.observer = new ResizeObserver(update);
      state.observer.observe(element);
    }
    states.set(element, state);
    update();
    reveal(element, state);
  },
  updated(element) {
    const state = states.get(element);
    if (!state) return;
    state.update();
    reveal(element, state);
  },
  unmounted(element) {
    const state = states.get(element);
    if (!state) return;
    element.removeEventListener("wheel", state.wheel);
    element.removeEventListener("scroll", state.update);
    state.observer?.disconnect();
    states.delete(element);
  }
};
