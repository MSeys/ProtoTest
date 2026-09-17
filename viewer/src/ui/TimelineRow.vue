<script setup lang="ts">
import type { TraceOutcome, TracePhase } from "../model/trace-schema";
import { formatDuration, pad, tone } from "../model/trace-format";

defineProps<{
  index: number;
  name: string;
  group?: string;
  outcome: TraceOutcome;
  duration: number;
  offset: number;
  width: number;
  segments: { phase: TracePhase; outcome: TraceOutcome; left: number; width: number }[];
  selected?: boolean;
}>();
defineEmits<{ select: [] }>();
</script>

<template>
  <div class="host">
    <button type="button" class="row" :class="[tone(outcome), { active: selected }]" :title="`${name} · ${outcome} · ${formatDuration(duration)}`" @click="$emit('select')">
      <b class="index">{{ pad(index) }}</b>
      <span class="name"><strong>{{ name }}</strong><small v-if="group">{{ group }}</small></span>
      <span class="track">
        <i class="bar" :style="{ left: `${offset}%`, width: `${width}%` }">
          <em v-for="segment in segments" :key="segment.phase"
              :style="{ left: `${segment.left}%`, width: `${segment.width}%`, background: `var(--phase-${segment.phase.toLocaleLowerCase()})` }" />
        </i>
      </span>
      <span class="duration"><i class="status" :class="tone(outcome)" />{{ formatDuration(duration) }}</span>
    </button>
  </div>
</template>

<style scoped>
.host { container-type: inline-size; }
.row {
  width: 100%;
  display: grid;
  grid-template-columns: 22px minmax(min(100%, 240px), 32%) minmax(0, 1fr) 82px;
  align-items: center;
  gap: var(--space-4);
  padding: var(--space-1) var(--space-2);
  border: 1px solid transparent;
  border-radius: var(--radius-control);
  background: transparent;
  text-align: left;
}
.row:hover { border-color: var(--border-strong); background: var(--hover); }
.row.active { border-color: var(--blueprint); background: var(--blueprint-soft); }
.index { color: var(--dim); font: var(--text-micro) var(--font-mono); }
.name { min-width: 0; display: flex; flex-direction: column; }
.name strong { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: var(--text-body); line-height: 1.35; }
.name small { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--muted); font-size: var(--text-micro); line-height: 1.25; }
.track { position: relative; height: 18px; border-radius: var(--radius-chip); background: var(--surface-2); }
.bar { position: absolute; inset-block: 0; min-width: 4px; border: 1px solid var(--border-strong); border-radius: var(--radius-chip); background: var(--surface-2); overflow: hidden; }
.row.success .bar { border-color: var(--success-line); }
.row.warning .bar { border-color: var(--warning); }
.row.danger .bar { border-color: var(--danger); }
.bar em { position: absolute; inset-block: 0; min-width: 2px; }
.duration { display: inline-flex; align-items: center; justify-content: flex-end; gap: var(--space-2); color: var(--muted); font: var(--text-micro) var(--font-mono); }

/* When the row itself is narrow, the bar gets its own line instead of collapsing to a sliver. */
@container (max-width: 620px) {
  .row { grid-template-columns: 22px minmax(0, 1fr) 82px; row-gap: var(--space-1); }
  .index { grid-area: 1 / 1; }
  .name { grid-area: 1 / 2; }
  .duration { grid-area: 1 / 3; }
  .track { grid-area: 2 / 2 / 3 / 4; }
}
</style>
