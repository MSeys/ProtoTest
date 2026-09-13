<script setup lang="ts">
import type { TestTrace, TraceEntry, TraceTreeItem } from "../trace-schema";
import { categoryForEntry, entrySummary, formatDuration, milliseconds, relativeTime, tone } from "../trace-utils";

defineOptions({ name: "TraceTreeNode" });
const props = defineProps<{
  item: TraceTreeItem;
  depth: number;
  test: TestTrace;
  selectedId?: string;
  collapsed: Set<string>;
  forceExpanded: boolean;
}>();
const emit = defineEmits<{ select: [entry: TraceEntry]; toggle: [id: string] }>();
</script>

<template>
  <div class="trace-row" :class="{ selected: selectedId === item.entry.id }" :style="{ '--depth': depth }">
    <button type="button" class="tree-toggle" :class="{ leaf: !item.children.length }"
            :title="item.children.length ? 'Expand or collapse' : 'Event'"
            @click="item.children.length && emit('toggle', item.entry.id)">
      {{ item.children.length ? ((collapsed.has(item.entry.id) && !forceExpanded) ? '+' : '−') : '·' }}
    </button>
    <button type="button" class="entry-select" @click="emit('select', item.entry)">
      <span class="entry-label">
        <i class="status" :class="tone(item.entry.outcome)" />
        <span class="entry-name">{{ item.entry.name }}</span>
        <small class="source">{{ entrySummary(item.entry) }}</small>
      </span>
      <span class="entry-tags"><em>{{ categoryForEntry(item.entry) }}</em><em>{{ item.entry.entryKind }}</em></span>
      <span class="entry-timing"><small>{{ relativeTime(item.entry, test) }}</small><strong>{{ item.entry.duration ? formatDuration(milliseconds(item.entry.duration)) : 'event' }}</strong></span>
    </button>
  </div>
  <template v-if="forceExpanded || !collapsed.has(item.entry.id)">
    <TraceTreeNode v-for="child in item.children" :key="child.entry.id" :item="child" :depth="depth + 1" :test="test"
                   :selected-id="selectedId" :collapsed="collapsed" :force-expanded="forceExpanded"
                   @select="emit('select', $event)" @toggle="emit('toggle', $event)" />
  </template>
</template>
