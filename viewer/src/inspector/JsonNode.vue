<script setup lang="ts">
import { computed, inject, ref } from "vue";
import type { JsonContext } from "./json";
import { jsonContextKey, parseEmbedded } from "./json";

defineOptions({ name: "JsonNode" });

const props = defineProps<{
  /** The key or index this value sits under; absent for the root. */
  name?: string | number;
  value: unknown;
  path: string;
  depth: number;
  /** Separates siblings the way JSON does, so the tree still reads as JSON. */
  last: boolean;
}>();

const context = inject(jsonContextKey) as JsonContext;

// A string that is itself JSON - a response body kept as text - opens as the document it is.
const embedded = computed(() => typeof props.value === "string" ? parseEmbedded(props.value) : undefined);
const value = computed(() => embedded.value ?? props.value);
const isContainer = computed(() => value.value !== null && typeof value.value === "object");
const isArray = computed(() => Array.isArray(value.value));
const entries = computed<[string | number, unknown][]>(() => {
  if (!isContainer.value) return [];
  return isArray.value
    ? (value.value as unknown[]).map((item, index) => [index, item])
    : Object.entries(value.value as Record<string, unknown>);
});
const open = computed(() => context.isOpen(props.path, props.depth));
const marked = computed(() => context.marks.has(props.path));

const LIMIT = 280;
const expandedText = ref(false);
const text = computed(() => {
  if (typeof value.value !== "string") return "";
  return expandedText.value || value.value.length <= LIMIT ? value.value : `${value.value.slice(0, LIMIT)}…`;
});
const kind = computed(() => value.value === null ? "null" : typeof value.value);
const summary = computed(() => isArray.value
  ? `${entries.value.length} ${entries.value.length === 1 ? "item" : "items"}`
  : `${entries.value.length} ${entries.value.length === 1 ? "field" : "fields"}`);
const key = computed(() => typeof props.name === "number" ? String(props.name) : props.name === undefined ? "" : `"${props.name}"`);
const childPath = (name: string | number) => typeof name === "number" ? `${props.path}[${name}]` : `${props.path}.${name}`;
</script>

<template>
  <div class="node" :class="{ marked }">
    <div class="line">
      <button v-if="isContainer && entries.length" type="button" class="toggle" :aria-expanded="open"
              :aria-label="open ? 'Collapse' : 'Expand'" @click="context.toggle(path, depth)">
        <svg viewBox="0 0 10 10" width="8" height="8" aria-hidden="true"><path d="M3 1.8 7 5 3 8.2" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" /></svg>
      </button>
      <span v-else class="toggle-space" />
      <span class="content">
        <template v-if="name !== undefined"><span class="key" :class="{ index: typeof name === 'number' }">{{ key }}</span><span class="punct">: </span></template>
        <template v-if="isContainer">
          <span class="punct">{{ isArray ? "[" : "{" }}</span>
          <template v-if="!open || !entries.length">
            <button v-if="entries.length" type="button" class="summary" @click="context.toggle(path, depth)">{{ summary }}</button>
            <span class="punct">{{ isArray ? "]" : "}" }}{{ last ? "" : "," }}</span>
          </template>
          <span v-if="embedded !== undefined" class="badge" title="Recorded as a string that contains JSON">JSON text</span>
        </template>
        <template v-else>
          <span class="scalar" :class="kind">{{ kind === "string" ? `"${text}"` : String(value) }}</span><span class="punct">{{ last ? "" : "," }}</span>
          <button v-if="kind === 'string' && (value as string).length > LIMIT" type="button" class="more" @click="expandedText = !expandedText">
            {{ expandedText ? "show less" : `show all ${(value as string).length} characters` }}
          </button>
        </template>
      </span>
    </div>
    <template v-if="isContainer && open && entries.length">
      <div class="children">
        <JsonNode v-for="([childName, child], index) in entries" :key="String(childName)" :name="childName" :value="child"
                  :path="childPath(childName)" :depth="depth + 1" :last="index === entries.length - 1" />
      </div>
      <div class="line close"><span class="toggle-space" /><span class="punct">{{ isArray ? "]" : "}" }}{{ last ? "" : "," }}</span></div>
    </template>
  </div>
</template>

<style scoped>
.line { display: grid; grid-template-columns: 14px minmax(0, 1fr); align-items: start; gap: var(--space-1); border-radius: var(--radius-hairline); }
.line:hover { background: var(--code-highlight); }
.node.marked > .line:first-child { background: color-mix(in srgb, var(--danger) 18%, transparent); box-shadow: inset 2px 0 0 var(--danger); }
.toggle { width: 14px; height: 18px; padding: 0; display: grid; place-items: center; border: 0; background: transparent; color: var(--code-punctuation); }
.toggle svg { transition: transform var(--motion-fast) var(--motion-ease); }
.toggle[aria-expanded="true"] svg { transform: rotate(90deg); }
.toggle:hover { color: var(--code-text); }
.content { min-width: 0; overflow-wrap: anywhere; }
.key { color: var(--code-attribute); }
.key.index { color: var(--code-comment); }
.punct { color: var(--code-punctuation); }
.scalar.string { color: var(--code-string); white-space: pre-wrap; }
.scalar.number { color: var(--code-number); }
.scalar.boolean, .scalar.null { color: var(--code-keyword); }
.summary { margin: 0 var(--space-1); padding: 0 var(--space-1); border: 0; border-radius: var(--radius-hairline); background: var(--code-highlight); color: var(--code-comment); font: inherit; }
.summary:hover { color: var(--code-text); }
.badge { margin-left: var(--space-2); padding: 0 var(--space-1); border-radius: var(--radius-hairline); background: var(--code-highlight); color: var(--code-comment); font-family: var(--font-ui); font-size: var(--text-micro); }
.more { margin-left: var(--space-2); padding: 0; border: 0; background: transparent; color: var(--code-keyword); font-family: var(--font-ui); font-size: var(--text-micro); }
.more:hover { text-decoration: underline; }
/* Nesting hangs off the same guide line the story and the shape tree use. */
.children { margin-left: var(--space-2); padding-left: var(--space-3); border-left: 1px solid color-mix(in srgb, var(--code-punctuation) 25%, transparent); }
</style>
