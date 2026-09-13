<script setup lang="ts">
import type { TraceEntry, TraceTreeItem } from "../trace-schema";
import { formatDuration, milliseconds, tone } from "../trace-utils";

defineOptions({ name: "TraceRelationNode" });
defineProps<{ item: TraceTreeItem; selectedId: string; depth: number }>();
const emit = defineEmits<{ select: [entry: TraceEntry] }>();
</script>

<template>
  <div class="relation-node" :class="{ current: item.entry.id === selectedId }" :style="{ '--relation-depth': depth }">
    <button type="button" @click="emit('select', item.entry)">
      <i class="status" :class="tone(item.entry.outcome)" />
      <span><strong>{{ item.entry.name }}</strong><small>{{ item.entry.kind }}</small></span>
      <em>{{ item.entry.duration ? formatDuration(milliseconds(item.entry.duration)) : 'event' }}</em>
    </button>
  </div>
  <TraceRelationNode v-for="child in item.children" :key="child.entry.id" :item="child" :selected-id="selectedId" :depth="depth + 1" @select="emit('select', $event)" />
</template>
