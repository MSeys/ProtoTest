import { onBeforeUnmount, ref } from "vue";

export interface ColumnResize {
  width: ReturnType<typeof ref<number | undefined>>;
  start(event: PointerEvent): void;
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

  function clamp(value: number): number {
    return Math.min(options.max, Math.max(options.min, value));
  }

  function move(event: PointerEvent) {
    const delta = options.edge === "leading" ? event.clientX - origin : origin - event.clientX;
    const next = clamp(originWidth + delta);
    cancelAnimationFrame(frame);
    frame = requestAnimationFrame(() => { width.value = next; });
  }

  function stop() {
    removeEventListener("pointermove", move);
    removeEventListener("pointerup", stop);
    removeEventListener("pointercancel", stop);
    document.body.classList.remove("is-resizing");
    try { if (width.value) localStorage.setItem(key, String(Math.round(width.value))); } catch { /* ignore */ }
  }

  function start(event: PointerEvent) {
    const column = (event.currentTarget as HTMLElement).previousElementSibling as HTMLElement | null;
    const neighbour = options.edge === "leading" ? column : (event.currentTarget as HTMLElement).nextElementSibling as HTMLElement | null;
    origin = event.clientX;
    originWidth = neighbour?.getBoundingClientRect().width ?? options.min;
    document.body.classList.add("is-resizing");
    addEventListener("pointermove", move);
    addEventListener("pointerup", stop);
    addEventListener("pointercancel", stop);
    event.preventDefault();
  }

  function reset() {
    width.value = undefined;
    try { localStorage.removeItem(key); } catch { /* ignore */ }
  }

  onBeforeUnmount(() => { cancelAnimationFrame(frame); stop(); });
  return { width, start, reset };
}
