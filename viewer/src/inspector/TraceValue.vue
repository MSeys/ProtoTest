<script setup lang="ts">
import { computed } from "vue";

const props = withDefaults(defineProps<{ value: unknown; bare?: boolean; expanded?: boolean }>(), {
  bare: false,
  expanded: false
});
const parsed = computed(() => {
  if (typeof props.value !== "string") return props.value;
  const trimmed = props.value.trim();
  if (!trimmed) return props.value;
  if (!trimmed.startsWith("{") && !trimmed.startsWith("[") && !/^(?:true|false|null|-?\d+(?:\.\d+)?)$/.test(trimmed)) return props.value;
  try { return JSON.parse(trimmed); } catch { return props.value; }
});
const isObject = computed(() => parsed.value !== null && typeof parsed.value === "object");
const count = computed(() => isObject.value ? Object.keys(parsed.value as object).length : 0);
const summary = computed(() => Array.isArray(parsed.value) ? `${count.value} items` : `${count.value} fields`);
const formatted = computed(() => JSON.stringify(parsed.value, null, 2) ?? String(parsed.value));
const scalarKind = computed(() => parsed.value === null ? "null" : typeof parsed.value);
const scalar = computed(() => typeof parsed.value === "string" ? JSON.stringify(parsed.value) : String(parsed.value));
</script>

<template>
  <pre v-if="isObject && bare" class="document"><code>{{ formatted }}</code></pre>
  <details v-else-if="isObject" class="value" :open="expanded">
    <summary><span>{{ Array.isArray(parsed) ? "[ ]" : "{ }" }}</span>{{ summary }}</summary>
    <pre class="document"><code>{{ formatted }}</code></pre>
  </details>
  <code v-else class="scalar" :class="scalarKind">{{ scalar }}</code>
</template>

<style scoped>
/* A recorded value is data: mono, on the sunken surface, and never reflowed into prose. */
.document {
  max-height: 320px;
  margin: 0;
  padding: var(--space-3);
  overflow: auto;
  border-radius: var(--radius-chip);
  background: var(--surface-sunken);
  color: var(--text-on-sunken);
  font: var(--text-micro)/1.6 var(--font-mono);
}
.document code { font: inherit; }
.value > summary {
  display: flex;
  align-items: center;
  gap: var(--space-2);
  color: var(--muted);
  font-size: var(--text-micro);
  cursor: pointer;
}
.value > summary::-webkit-details-marker { display: none; }
.value > summary span { color: var(--blueprint); font-family: var(--font-mono); }
.value[open] > summary { margin-bottom: var(--space-2); }
.scalar { font: var(--text-micro) var(--font-mono); overflow-wrap: anywhere; }
.scalar.string { color: var(--success); }
.scalar.number { color: var(--pt-cyan); }
.scalar.boolean, .scalar.null { color: var(--violet); }
</style>
