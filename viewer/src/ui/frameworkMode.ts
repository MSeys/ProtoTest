import { ref, watch } from "vue";

/*
 * How the views treat the framework's own operations: hooks, extensions, clients, resources. One choice for
 * every view, kept for the next visit; the default dims them, so the scenario leads without hiding anything.
 */
export type FrameworkMode = "show" | "dim" | "hide";

const key = "prototrace.framework";

function read(): FrameworkMode {
  try {
    const value = localStorage.getItem(key);
    if (value === "show" || value === "dim" || value === "hide") return value;
  } catch { /* storage can be blocked; the default still works */ }
  return "dim";
}

export const frameworkMode = ref<FrameworkMode>(read());

watch(frameworkMode, value => {
  try { localStorage.setItem(key, value); } catch { /* a blocked store only loses the memory */ }
});
