<script setup lang="ts">
import { computed } from "vue";
import type { TestTrace } from "../trace/model";
import { untracedGaps } from "../trace/analysis";
import { formatDuration, timelinePercent } from "../trace/format";

/*
 * The test on its own clock: each phase as a segment, time with no recorded operation hatched over it,
 * and the moment the failure happened. The legend under it says the same in words.
 */
const props = defineProps<{ test: TestTrace }>();

const total = computed(() => Math.max(props.test.duration, 1));
const lifecycles = computed(() => props.test.roots.filter(span => span.kind === `test.${span.phase}`));
const gaps = computed(() => untracedGaps(props.test));
const place = (start: number, duration: number) => ({
  left: `${timelinePercent(start, props.test.start, total.value)}%`,
  width: `${Math.max(0.4, (duration / total.value) * 100)}%`
});
const legend = computed(() => lifecycles.value.map(span => ({
  phase: span.phase,
  duration: span.duration,
  untraced: gaps.value.filter(gap => gap.lifecycle === span).reduce((sum, gap) => sum + gap.duration, 0)
})));
const title = (phase: string) => phase.charAt(0).toUpperCase() + phase.slice(1);
</script>

<template>
  <div v-if="lifecycles.length" class="band">
    <div class="bar" role="img" :aria-label="legend.map(entry => `${title(entry.phase)} ${formatDuration(entry.duration)}`).join(', ')">
      <i v-for="span in lifecycles" :key="span.id" class="segment" :style="{ ...place(span.start, span.duration), background: `var(--phase-${span.phase})` }" />
      <i v-for="gap in gaps" :key="`${gap.phase}-${gap.start}`" class="untraced" :style="place(gap.start, gap.duration)" />
      <i v-if="test.failure" class="failure" :style="{ left: `${timelinePercent(test.failure.span.start, test.start, total)}%` }" />
    </div>
    <p class="legend">
      <span v-for="entry in legend" :key="entry.phase">
        <i :style="{ background: `var(--phase-${entry.phase})` }" aria-hidden="true" />{{ title(entry.phase) }} <b>{{ formatDuration(entry.duration) }}</b><em v-if="entry.untraced">, {{ formatDuration(entry.untraced) }} without an operation</em>
      </span>
    </p>
  </div>
</template>

<style scoped>
.band { display: grid; gap: var(--space-1); }
.bar { position: relative; height: 10px; border-radius: var(--radius-hairline); background: var(--surface-2); overflow: hidden; }
.bar i { position: absolute; top: 0; bottom: 0; }
.segment { min-width: 2px; opacity: .7; }
.untraced { background: repeating-linear-gradient(135deg, color-mix(in srgb, var(--text) 55%, transparent) 0 2px, transparent 2px 5px); }
.failure { width: 2px; margin-left: -1px; background: var(--danger); }
.legend { display: flex; flex-wrap: wrap; gap: var(--space-1) var(--space-5); color: var(--muted); font-size: var(--text-meta); }
.legend span { display: inline-flex; align-items: center; gap: var(--space-1); }
.legend i { width: 10px; height: 6px; border-radius: var(--radius-hairline); opacity: .7; }
.legend b { color: var(--text); font-weight: var(--weight-semibold); }
.legend em { color: var(--text); font-style: normal; }
</style>
