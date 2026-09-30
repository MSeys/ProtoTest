<script setup lang="ts">
import type { ShapeCheckNode } from "../trace/shapes";
import ShapeResultNode from "./ShapeResultNode.vue";
import EmptyState from "../ui/EmptyState.vue";
import DetailCard from "../ui/DetailCard.vue";

defineProps<{ nodes: ShapeCheckNode[] }>();
</script>

<template>
  <DetailCard title="Validated document">
    <template #tools><small class="legend"><b>✓</b> matched <em>×</em> expected, then actual</small></template>
    <div v-if="nodes.length" class="root">
      <ShapeResultNode v-for="node in nodes" :key="node.path" :node="node" />
    </div>
    <EmptyState v-else message="No property-level diagnostics were recorded for this check." />
  </DetailCard>
</template>

<style scoped>
.legend b { color: var(--success); }
.legend em { color: var(--danger); font-style: normal; }
.root { padding: var(--space-2); background: var(--surface); font-family: var(--font-mono); }
</style>
