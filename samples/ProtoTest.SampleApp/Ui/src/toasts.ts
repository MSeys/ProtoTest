import { reactive } from "vue";

export type ToastTone = "success" | "error" | "info";

export interface Toast {
  id: number;
  tone: ToastTone;
  message: string;
}

export const toasts = reactive<Toast[]>([]);
let nextId = 1;

/** A fixed-corner notice; it never moves the layout under it. */
export function toast(tone: ToastTone, message: string, ttl = 5000): void {
  const id = nextId++;
  toasts.push({ id, tone, message });
  window.setTimeout(() => dismiss(id), ttl);
}

export function dismiss(id: number): void {
  const index = toasts.findIndex((item) => item.id === id);
  if (index >= 0) {
    toasts.splice(index, 1);
  }
}
