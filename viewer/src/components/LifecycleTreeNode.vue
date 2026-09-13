<script setup lang="ts">
import { computed, ref } from "vue";
import type { TestTrace, TraceEntry, TraceTreeItem } from "../trace-schema";
import { formatDuration, milliseconds, tone } from "../trace-utils";

defineOptions({ name: "LifecycleTreeNode" });
const props = defineProps<{
  item: TraceTreeItem;
  depth: number;
  test: TestTrace;
  start: number;
  duration: number;
}>();
const emit = defineEmits<{ select: [entry: TraceEntry] }>();
const collapsed = ref(false);
const offset = computed(() => Math.max(0, ((Date.parse(props.item.entry.timestampUtc) - props.start) / props.duration) * 100));
const width = computed(() => props.item.entry.duration
  ? Math.max(.35, (milliseconds(props.item.entry.duration) / props.duration) * 100)
  : .35);
</script>

<template>
  <div class="lifecycle-tree-node">
    <div class="lifecycle-entry" :class="{ root: depth === 0 }" :style="{ '--tree-depth': depth }">
      <button type="button" class="lifecycle-disclosure" :class="{ leaf: !item.children.length }"
              :aria-label="item.children.length ? 'Expand or collapse operation' : 'Trace event'"
              @click="item.children.length && (collapsed = !collapsed)">
        {{ item.children.length ? (collapsed ? '+' : '−') : '·' }}
      </button>
      <button type="button" class="lifecycle-entry-main" @click="emit('select', item.entry)">
        <span class="timeline-entry-label"><strong>{{ item.entry.name }}</strong><small>{{ item.entry.kind }}<template v-if="item.children.length"> · {{ item.children.length }} children</template></small></span>
        <span class="timeline-track"><i :class="[item.entry.entryKind.toLocaleLowerCase(), tone(item.entry.outcome)]" :style="{ left: `${offset}%`, width: `${width}%` }" /></span>
        <em>{{ item.entry.duration ? formatDuration(milliseconds(item.entry.duration)) : 'event' }}</em>
      </button>
    </div>
    <div v-if="item.children.length && !collapsed" class="lifecycle-children">
      <LifecycleTreeNode v-for="child in item.children" :key="child.entry.id" :item="child" :depth="depth + 1"
                         :test="test" :start="start" :duration="duration" @select="emit('select', $event)" />
    </div>
  </div>
</template>
