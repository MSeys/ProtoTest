<script setup lang="ts">
import { demos, formatDemoFacts, type DemoFacts } from "../demos";

defineProps<{ facts: Record<string, DemoFacts | null> }>();
defineEmits<{ open: [key: string] }>();
</script>

<template>
  <ul class="demo-list">
    <li v-for="entry in demos" :key="entry.key">
      <button type="button" class="demo-row" @click.stop="$emit('open', entry.key)" @keydown.stop>
        <span class="demo-main">
          <strong>{{ entry.label }}</strong>
          <small>{{ entry.description }}</small>
        </span>
        <small v-if="facts[entry.key]" class="demo-facts">{{ formatDemoFacts(facts[entry.key]!) }}</small>
        <small v-else-if="!(entry.key in facts)" class="demo-facts">Reading…</small>
      </button>
    </li>
  </ul>
</template>

<style scoped>
.demo-list { margin: 0; padding: 0; display: grid; list-style: none; border: 1px solid var(--border); border-radius: var(--radius-control); background: var(--surface); }
.demo-row {
  width: 100%;
  padding: var(--space-2) var(--space-4);
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  align-items: center;
  gap: var(--space-3);
  border: 0;
  border-top: 1px solid var(--border);
  background: transparent;
  color: var(--text);
  text-align: left;
  cursor: pointer;
}
li:first-child .demo-row { border-top: 0; }
.demo-row:hover { background: var(--hover); }
.demo-main { min-width: 0; display: grid; gap: 2px; }
.demo-main strong { font-size: var(--text-meta); font-weight: var(--weight-semibold); }
.demo-main small { overflow-wrap: anywhere; color: var(--muted); font-size: var(--text-micro); }
.demo-facts { color: var(--dim); font: var(--text-micro) var(--font-mono); white-space: nowrap; }
</style>
