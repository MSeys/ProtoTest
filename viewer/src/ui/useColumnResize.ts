import { onBeforeUnmount, ref } from "vue";

export interface ColumnResize {
  width: ReturnType<typeof ref<number | undefined>>;
  start(event: PointerEvent): void;
  /** Arrow-key resize: the pixels move toward the pointer side, from the width the column has right now. */
  nudge(event: KeyboardEvent, pixels: number): void;
  reset(): void;
}

/**
 * A draggable column width, remembered per reader. The width is undefined until it is dragged, so the
 * layout keeps its responsive default (a clamp) rather than freezing at whatever the first viewport was.
 */
export function useColumnResize(
  key: string,
  options: { min: number; max: number; edge: "leading" | "trailing" }
): ColumnResize {
  let stored: number | undefined;
  try {
    const saved = Number(localStorage.getItem(key));
    if (Number.isFinite(saved) && saved > 0) stored = saved;
  } catch { /* Storage may be disabled; the responsive default still applies. */ }

  const width = ref<number | undefined>(stored);
  let frame = 0;
  let origin = 0;
  let originWidth = 0;

  // The column is the nearest sibling that takes up room: a hidden control between them (the sheet's grabber
  // on a wide screen) must not be measured as the column, or the drag starts from zero and jumps.
  function visibleSibling(element: HTMLElement, direction: "previous" | "next"): HTMLElement | null {
    let sibling = direction === "previous" ? element.previousElementSibling : element.nextElementSibling;
    while (sibling && (sibling as HTMLElement).getBoundingClientRect().width === 0) {
      sibling = direction === "previous" ? sibling.previousElementSibling : sibling.nextElementSibling;
    }
    return sibling as HTMLElement | null;
  }

  function clamp(value: number): number {
    return Math.min(options.max, Math.max(options.min, value));
  }

  function move(event: PointerEvent) {
    const delta = options.edge === "leading" ? event.clientX - origin : origin - event.clientX;
    const next = clamp(originWidth + delta);
    cancelAnimationFrame(frame);
    frame = requestAnimationFrame(() => { width.value = next; });
  }

  function persist() {
    try { if (width.value) localStorage.setItem(key, String(Math.round(width.value))); } catch { /* ignore */ }
  }

  function stop() {
    removeEventListener("pointermove", move);
    removeEventListener("pointerup", stop);
    removeEventListener("pointercancel", stop);
    document.body.classList.remove("is-resizing");
    persist();
  }

  function start(event: PointerEvent) {
    const neighbour = visibleSibling(event.currentTarget as HTMLElement, options.edge === "leading" ? "previous" : "next");
    origin = event.clientX;
    originWidth = neighbour?.getBoundingClientRect().width ?? options.min;
    document.body.classList.add("is-resizing");
    addEventListener("pointermove", move);
    addEventListener("pointerup", stop);
    addEventListener("pointercancel", stop);
    event.preventDefault();
  }

  function nudge(event: KeyboardEvent, pixels: number) {
    const neighbour = visibleSibling(event.currentTarget as HTMLElement, options.edge === "leading" ? "previous" : "next");
    const current = width.value ?? neighbour?.getBoundingClientRect().width ?? options.min;
    width.value = clamp(current + (options.edge === "leading" ? pixels : -pixels));
    persist();
  }

  function reset() {
    width.value = undefined;
    try { localStorage.removeItem(key); } catch { /* ignore */ }
  }

  onBeforeUnmount(() => { cancelAnimationFrame(frame); stop(); });
  return { width, start, nudge, reset };
}
