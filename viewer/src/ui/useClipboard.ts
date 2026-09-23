import { ref } from "vue";

/**
 * Copies text and reports "Copied" for a moment. A clipboard that is unavailable stays silent: the text
 * remains selectable where it is, which is the fallback the inspector offers.
 */
export function useClipboard(durationMs = 1400) {
  const copied = ref(false);

  async function copy(text: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(text);
      copied.value = true;
      setTimeout(() => { copied.value = false; }, durationMs);
    } catch { /* the clipboard can be unavailable; the text stays selectable */ }
  }

  return { copied, copy };
}
