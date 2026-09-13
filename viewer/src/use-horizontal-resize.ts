import { ref } from "vue";

export function useHorizontalResize(
  storageKey: string,
  initialSize: number,
  minimumSize: number,
  maximumSize: () => number,
  direction = 1) {
  const stored = Number.parseInt(localStorage.getItem(storageKey) ?? "", 10);
  const clamp = (value: number) => Math.min(Math.max(minimumSize, maximumSize()), Math.max(minimumSize, value));
  const size = ref(clamp(Number.isFinite(stored) ? stored : initialSize));

  function setSize(value: number) {
    size.value = clamp(value);
    localStorage.setItem(storageKey, String(Math.round(size.value)));
  }

  function resizeBy(delta: number) {
    setSize(size.value + delta);
  }

  function startResize(event: PointerEvent) {
    event.preventDefault();
    const startX = event.clientX;
    const startSize = size.value;
    document.body.classList.add("is-resizing");

    const move = (moveEvent: PointerEvent) => {
      size.value = clamp(startSize + ((moveEvent.clientX - startX) * direction));
    };
    const stop = () => {
      setSize(size.value);
      document.body.classList.remove("is-resizing");
      window.removeEventListener("pointermove", move);
      window.removeEventListener("pointerup", stop);
    };
    window.addEventListener("pointermove", move);
    window.addEventListener("pointerup", stop, { once: true });
  }

  return { size, startResize, resizeBy };
}
