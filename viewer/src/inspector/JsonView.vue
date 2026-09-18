<script setup lang="ts">
import { computed, provide, ref, watch } from "vue";
import JsonNode from "./JsonNode.vue";
import { jsonContextKey, jsonPath, parseEmbedded } from "./json";

/*
 * A recorded document, read as JSON: keys, strings, numbers and literals in the code palette, every object and
 * array foldable, folded ones summarised by their size. It wraps rather than scrolls sideways - the inspector's
 * body is the one scroller - and a value that is not JSON is shown as the text it is.
 */
const props = withDefaults(defineProps<{
  value: unknown;
  /** A heading for the document, such as "Body" or "Response". */
  label?: string;
  /** How many levels open at first: 1 shows the top-level fields, 0 starts folded. */
  openDepth?: number;
  /** Paths to mark, in shape-check notation ($.a.b[0]). */
  marks?: string[];
}>(), { openDepth: 1, marks: () => [] });

const parsed = computed<unknown>(() => {
  if (typeof props.value !== "string") return props.value;
  return parseEmbedded(props.value) ?? props.value;
});
const isDocument = computed(() => parsed.value !== null && typeof parsed.value === "object");
const size = computed(() => {
  if (!isDocument.value) return "";
  const count = Array.isArray(parsed.value) ? parsed.value.length : Object.keys(parsed.value as object).length;
  return Array.isArray(parsed.value) ? `${count} ${count === 1 ? "item" : "items"}` : `${count} ${count === 1 ? "field" : "fields"}`;
});

const mode = ref<"default" | "all" | "none">("default");
const overrides = ref(new Map<string, boolean>());
watch(() => props.value, () => { mode.value = "default"; overrides.value = new Map(); });

function isOpen(path: string, depth: number): boolean {
  const override = overrides.value.get(path);
  if (override !== undefined) return override;
  if (mode.value === "all") return true;
  if (mode.value === "none") return false;
  return depth < props.openDepth;
}
function toggle(path: string, depth: number) {
  const next = new Map(overrides.value);
  next.set(path, !isOpen(path, depth));
  overrides.value = next;
}
function setAll(value: "all" | "none") {
  mode.value = value;
  overrides.value = new Map();
}

const marks = computed(() => new Set(props.marks.map(jsonPath)));
provide(jsonContextKey, { isOpen, toggle, get marks() { return marks.value; } });

const copied = ref(false);
async function copy() {
  const text = isDocument.value ? JSON.stringify(parsed.value, null, 2) : String(parsed.value ?? "");
  try {
    await navigator.clipboard.writeText(text);
    copied.value = true;
    setTimeout(() => { copied.value = false; }, 1400);
  } catch { /* the clipboard can be unavailable; the text stays selectable */ }
}
</script>

<template>
  <section class="json">
    <header>
      <strong v-if="label">{{ label }}</strong>
      <span class="size">{{ size }}</span>
      <span class="tools">
        <template v-if="isDocument">
          <button type="button" @click="setAll('all')">Expand all</button>
          <button type="button" @click="setAll('none')">Collapse all</button>
        </template>
        <button type="button" @click="copy">{{ copied ? "Copied" : "Copy" }}</button>
      </span>
    </header>
    <div class="body">
      <JsonNode v-if="isDocument" :value="parsed" path="$" :depth="0" last />
      <pre v-else class="text">{{ String(parsed ?? "") }}</pre>
    </div>
  </section>
</template>

<style scoped>
.json { min-width: 0; border: 1px solid var(--border); border-radius: var(--radius-control); overflow: clip; }
header {
  min-height: var(--row-height);
  padding: var(--space-1) var(--space-2) var(--space-1) var(--space-3);
  display: flex;
  align-items: center;
  gap: var(--space-2);
  border-bottom: 1px solid var(--border);
  background: var(--surface-2);
}
header strong { font-size: var(--text-meta); font-weight: var(--weight-bold); }
.size { color: var(--muted); font-size: var(--text-micro); }
.tools { margin-left: auto; display: flex; gap: var(--space-1); }
.tools button {
  height: 20px;
  padding: 0 var(--space-2);
  border: 1px solid transparent;
  border-radius: var(--radius-chip);
  background: transparent;
  color: var(--muted);
  font-size: var(--text-micro);
  transition: color var(--motion-fast) var(--motion-ease), border-color var(--motion-fast) var(--motion-ease);
}
.tools button:hover { border-color: var(--border); color: var(--text); }
.body {
  padding: var(--space-2) var(--space-3);
  background: var(--surface-sunken);
  color: var(--code-text);
  font: var(--text-micro)/var(--leading) var(--font-mono);
}
.text { margin: 0; white-space: pre-wrap; overflow-wrap: anywhere; font: inherit; }
</style>
