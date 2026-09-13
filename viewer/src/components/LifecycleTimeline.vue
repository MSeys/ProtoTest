<script setup lang="ts">
import { computed } from "vue";
import type { TestTrace, TraceEntry } from "../trace-schema";
import { buildTree, formatDuration, milliseconds, phaseOrder, phaseSummary, testDisplayName, tone } from "../trace-utils";
import { useHorizontalResize } from "../use-horizontal-resize";
import LifecycleTreeNode from "./LifecycleTreeNode.vue";

const props = defineProps<{ test: TestTrace }>();
const emit = defineEmits<{ select: [entry: TraceEntry] }>();
const treeColumn = useHorizontalResize("prototrace.lifecycle-tree-width", 220, 160, () => Math.min(520, window.innerWidth * .5));
const start = computed(() => Date.parse(props.test.startedAtUtc));
const duration = computed(() => Math.max(milliseconds(props.test.duration), 0.01));
const phases = computed(() => {
  const completeTree = buildTree(props.test.entries);
  const collect = (items: typeof completeTree): TraceEntry[] => items.flatMap(item => [item.entry, ...collect(item.children)]);
  return phaseOrder
    .map(phase => {
      // A lifecycle lane owns its complete subtree. This also keeps traces produced
      // before phase inheritance was enforced from splitting children from parents.
      const tree = completeTree.filter(item => item.entry.phase === phase);
      const entries = collect(tree);
      return { ...phaseSummary(props.test, phase, entries), entries, tree };
    })
    .filter(phase => phase.entries.length);
});
</script>

<template>
  <section class="lifecycle-timeline" :style="{ '--lifecycle-tree-width': `${treeColumn.size.value}px` }">
    <header class="timeline-heading"><div><span class="eyebrow">Lifecycle timeline</span><h2 :title="test.name">{{ testDisplayName(test) }}</h2></div><strong>{{ formatDuration(duration) }}</strong></header>
    <div class="timeline-columns">
      <div class="timeline-tree-heading">
        <span>Tree</span>
        <div class="timeline-tree-resizer" role="separator" aria-label="Resize lifecycle tree column" aria-orientation="vertical"
             :aria-valuenow="Math.round(treeColumn.size.value)" tabindex="0" @pointerdown="treeColumn.startResize"
             @keydown.left.prevent="treeColumn.resizeBy(-20)" @keydown.right.prevent="treeColumn.resizeBy(20)" />
      </div>
      <div class="timeline-scale"><span v-for="tick in 6" :key="tick">{{ formatDuration(duration * (tick - 1) / 5) }}</span></div>
      <span />
    </div>
    <section v-for="phase in phases" :key="phase.phase" class="lifecycle-lane" :class="`phase-${phase.phase.toLocaleLowerCase()}`">
      <header><div><span class="phase-marker" /><strong>{{ phase.phase }}</strong></div><small>{{ phase.operations }} operations · {{ phase.events }} events</small><i class="status" :class="tone(phase.outcome)" /></header>
      <div class="lifecycle-tree">
        <LifecycleTreeNode v-for="item in phase.tree" :key="item.entry.id" :item="item" :depth="0"
                           :test="test" :start="start" :duration="duration" @select="emit('select', $event)" />
      </div>
    </section>
  </section>
</template>
