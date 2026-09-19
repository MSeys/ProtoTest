<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { fileName, readSource, type SourceLocation } from "../trace/sources";

const props = withDefaults(defineProps<{ location: SourceLocation; context?: number }>(), { context: 4 });

const text = ref<string>();
watch(() => props.location.file, async file => {
  text.value = undefined;
  const content = await readSource(file);
  if (props.location.file === file) text.value = content;
}, { immediate: true });

// The recorded line with a few lines either side; the file stays in the trace, so a wider view is a later step.
const lines = computed(() => {
  if (text.value === undefined) return [];
  const all = text.value.split(/\r?\n/);
  const first = Math.max(1, props.location.line - props.context);
  const last = Math.min(all.length, props.location.line + props.context);
  return all.slice(first - 1, last).map((content, index) => ({ number: first + index, content }));
});
// Strip the shared indentation so a method deep in a class still reads from the left edge.
const indent = computed(() => Math.min(...lines.value.filter(line => line.content.trim()).map(line => line.content.match(/^\s*/)![0].length)));
</script>

<template>
  <section class="source">
    <h3>
      <span :title="location.file">{{ fileName(location.file) }}:{{ location.line }}</span>
      <small v-if="location.functionName">{{ location.functionName }}</small>
    </h3>
    <pre v-if="lines.length"><code><span v-for="line in lines" :key="line.number" class="line" :class="{ current: line.number === location.line }"><b>{{ line.number }}</b>{{ line.content.slice(Number.isFinite(indent) ? indent : 0) || " " }}
</span></code></pre>
  </section>
</template>

<style scoped>
.source { min-width: 0; display: grid; gap: var(--space-2); }
h3 { display: flex; flex-wrap: wrap; align-items: baseline; gap: var(--space-1) var(--space-2); font-family: var(--font-mono); font-size: var(--text-meta); font-weight: var(--weight-bold); letter-spacing: 0; }
h3 small { min-width: 0; color: var(--dim); font-family: var(--font-ui); font-size: var(--text-micro); font-weight: var(--weight-regular); overflow-wrap: anywhere; }
pre { margin: 0; padding: var(--space-2) 0; overflow-x: auto; border-radius: var(--radius-control); background: var(--surface-sunken); color: var(--code-text); font: var(--text-micro)/var(--leading) var(--font-mono); }
.line { display: block; padding-right: var(--space-3); white-space: pre; }
.line b { display: inline-block; width: 4ch; margin-right: var(--space-3); color: var(--code-comment); font-weight: var(--weight-regular); text-align: right; }
.line.current { background: var(--code-highlight); box-shadow: inset 2px 0 0 var(--blueprint); }
.line.current b { color: var(--code-text); }
</style>
