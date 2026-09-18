<script setup lang="ts">
import type { ShapeCheckNode } from "../trace/shapes";
import ShapeResultNode from "./ShapeResultNode.vue";
import EmptyState from "../ui/EmptyState.vue";

defineProps<{ nodes: ShapeCheckNode[] }>();
</script>

<template>
  <section class="shape">
    <header>
      <strong>Validated document</strong>
      <small><b>✓</b> matched <em>×</em> expected, then actual</small>
    </header>
    <div v-if="nodes.length" class="root">
      <ShapeResultNode v-for="node in nodes" :key="node.path" :node="node" />
    </div>
    <EmptyState v-else message="No property-level diagnostics were recorded for this check." />
  </section>
</template>

<style scoped>
.shape { border: 1px solid var(--border); border-radius: var(--radius-control); overflow: hidden; }
header {
  padding: var(--space-2) var(--space-3);
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  justify-content: space-between;
  gap: var(--space-2);
  border-bottom: 1px solid var(--border);
  background: var(--surface-2);
}
header strong { font-size: var(--text-meta); }
header small { color: var(--muted); font: var(--text-micro) var(--font-mono); }
header b { color: var(--success); }
header em { color: var(--danger); font-style: normal; }
.root { padding: var(--space-2); font-family: var(--font-mono); }
</style>
