<script setup lang="ts">
import { computed } from "vue";
import type { ShapeMismatch } from "../trace-schema";
import { buildShapeTree } from "../trace-utils";
import ShapeResultNode from "./ShapeResultNode.vue";

const props = defineProps<{ matches: string[]; mismatches: ShapeMismatch[]; expected?: unknown; actual?: unknown }>();
const nodes = computed(() => buildShapeTree(props.matches, props.mismatches, props.expected, props.actual));
</script>

<template>
  <section class="shape-result-tree">
    <header><span class="drawing-label">Validated document</span><small><b>✓</b> matched value · <em>×</em> expected → actual</small></header>
    <div v-if="nodes.length" class="shape-tree-root">
      <ShapeResultNode v-for="node in nodes" :key="node.path" :node="node" />
    </div>
    <div v-else class="inspector-empty">No property-level diagnostics were recorded for this check.</div>
  </section>
</template>
