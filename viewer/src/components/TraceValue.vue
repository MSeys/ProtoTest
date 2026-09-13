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
const opening = computed(() => Array.isArray(parsed.value) ? "[" : "{");
const closing = computed(() => Array.isArray(parsed.value) ? "]" : "}");
const formatted = computed(() => JSON.stringify(parsed.value, null, 2) ?? String(parsed.value));
const scalarKind = computed(() => parsed.value === null ? "null" : typeof parsed.value);
const scalar = computed(() => typeof parsed.value === "string" ? JSON.stringify(parsed.value) : String(parsed.value));
</script>

<template>
  <pre v-if="isObject && bare" class="trace-json-document"><code>{{ formatted }}</code></pre>
  <details v-else-if="isObject" class="trace-value" :open="expanded">
    <summary><code>{{ opening }}</code><span>{{ summary }}</span><code>{{ closing }}</code></summary>
    <pre class="trace-json-document"><code>{{ formatted }}</code></pre>
  </details>
  <code v-else class="trace-scalar" :class="`value-${scalarKind}`">{{ scalar }}</code>
</template>
