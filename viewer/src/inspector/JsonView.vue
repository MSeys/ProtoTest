<script setup lang="ts">
import { computed, provide, ref, watch } from "vue";
import JsonNode from "./JsonNode.vue";
import { jsonContextKey, jsonPath, parseEmbedded } from "./json";
import { plural } from "../trace/format";
import { useClipboard } from "../ui/useClipboard";
import DetailCard from "../ui/DetailCard.vue";

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
  return Array.isArray(parsed.value) ? `${count} ${plural(count, "item")}` : `${count} ${plural(count, "field")}`;
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

const { copied, copy } = useClipboard();
async function copyDocument() {
  const text = isDocument.value ? JSON.stringify(parsed.value, null, 2) : String(parsed.value ?? "");
  await copy(text);
}
</script>

<template>
  <DetailCard :title="label" :meta="size">
    <template #tools>
      <template v-if="isDocument">
        <button type="button" title="Open every level" @click="setAll('all')">Expand</button>
        <button type="button" title="Fold every level" @click="setAll('none')">Collapse</button>
      </template>
      <button type="button" @click="copyDocument">{{ copied ? "Copied" : "Copy" }}</button>
    </template>
    <div class="body">
      <JsonNode v-if="isDocument" :value="parsed" path="$" :depth="0" last />
      <pre v-else class="text">{{ String(parsed ?? "") }}</pre>
    </div>
  </DetailCard>
</template>

<style scoped>
.body {
  padding: var(--space-2) var(--space-3);
  color: var(--code-text);
  font: var(--text-micro)/var(--leading) var(--font-mono);
}
.text { margin: 0; white-space: pre-wrap; overflow-wrap: anywhere; font: inherit; }
</style>
