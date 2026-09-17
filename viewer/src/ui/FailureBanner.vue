<script setup lang="ts">
import type { TraceEntry } from "../model/trace-schema";
import { entryTitle, tone } from "../model/trace-format";
import { nodeType } from "../model/trace-levels";
import KindChip from "./KindChip.vue";

defineProps<{ entry: TraceEntry; outcome: string }>();
defineEmits<{ inspect: [] }>();
</script>

<template>
  <button type="button" class="banner" :class="tone(outcome as never)" @click="$emit('inspect')">
    <i class="status" :class="tone(outcome as never)" />
    <KindChip :type="nodeType(entry)" />
    <span class="what mono">{{ entryTitle(entry) }}</span>
    <span class="why">{{ entry.error?.message ?? entry.name }}</span>
    <span class="action">Inspect failure</span>
  </button>
</template>

<style scoped>
.banner {
  width: 100%;
  padding: var(--space-2) var(--space-4);
  display: grid;
  grid-template-columns: 7px auto auto minmax(0, 1fr) auto;
  align-items: center;
  gap: var(--space-3);
  border: 1px solid var(--border);
  border-left: 3px solid var(--dim);
  border-radius: var(--radius-control);
  background: var(--surface);
  text-align: left;
}
.banner.danger { border-left-color: var(--danger); background: var(--danger-soft); }
.banner.warning { border-left-color: var(--warning); background: var(--warning-soft); }
.banner:hover { border-color: var(--border-strong); }
.what { flex: none; font-size: var(--text-meta); font-weight: var(--weight-semibold); }
.why { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--muted); font-size: var(--text-meta); }
.action { flex: none; color: var(--blueprint); font-size: var(--text-meta); font-weight: var(--weight-semibold); }

@container (max-width: 760px) {
  .banner { grid-template-columns: 7px auto minmax(0, 1fr); row-gap: var(--space-1); }
  .why { grid-column: 2 / -1; white-space: normal; }
  .action { grid-column: 2 / -1; }
}
</style>
