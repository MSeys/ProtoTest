<script setup lang="ts">
import { computed } from "vue";
import type { TestTrace, TraceEntry, TracePhase } from "../trace-schema";
import { categoryDefinitions, categoryForEntry, entrySummary, formatDuration, milliseconds, overviewEntries, phaseOrder, phaseSummary, primaryFailure, testDisplayName, testGroupName, testGroupNamespace, tone } from "../trace-utils";

const props = defineProps<{ test: TestTrace }>();
const emit = defineEmits<{ select: [entry: TraceEntry] }>();

const phases = computed(() => phaseOrder
  .map(phase => phaseSummary(props.test, phase))
  .filter(summary => summary.entries.length));
const categories = computed(() => categoryDefinitions(props.test.entries)
  .map(category => ({
    ...category,
    entries: overviewEntries(category.id, props.test.entries),
    traceCount: props.test.entries.filter(entry => categoryForEntry(entry) === category.id).length
  }))
  .filter(category => category.entries.length));
const failure = computed(() => primaryFailure(props.test));

function phaseClass(phase: TracePhase) { return `phase-${phase.toLocaleLowerCase()}`; }
</script>

<template>
  <section class="test-overview">
    <header class="story-heading">
      <div>
        <span class="eyebrow">Test story</span>
        <h2 :title="test.name">{{ testDisplayName(test) }}</h2>
        <p>{{ testGroupName(test) }}<template v-if="testGroupNamespace(test)"> · {{ testGroupNamespace(test) }}</template></p>
      </div>
      <span class="outcome-pill" :class="tone(test.outcome)">{{ test.outcome }} · {{ formatDuration(milliseconds(test.duration)) }}</span>
    </header>

    <button v-if="failure" type="button" class="failure-banner" @click="emit('select', failure)">
      <span class="status danger" /><span><strong>{{ failure.name }}</strong><small>{{ failure.kind }} · {{ failure.error?.message ?? 'This operation failed.' }}</small></span><b>Inspect failure →</b>
    </button>

    <div class="lifecycle-strip" aria-label="Test lifecycle">
      <article v-for="phase in phases" :key="phase.phase" class="phase-card" :class="phaseClass(phase.phase)">
        <div class="phase-title"><span>{{ phase.phase }}</span><i class="status" :class="tone(phase.outcome)" /></div>
        <strong>{{ formatDuration(phase.duration) }}</strong>
        <small>{{ phase.operations }} operations · {{ phase.events }} events</small>
      </article>
    </div>

    <div class="story-grid">
      <article v-for="category in categories" :key="category.id" class="story-card">
        <header><span class="category-glyph">{{ category.glyph }}</span><div><strong>{{ category.label }}</strong><small>{{ category.entries.length }} summarized · {{ category.traceCount }} trace entries</small></div></header>
        <div class="story-items">
          <button v-for="entry in category.entries.slice(0, 3)" :key="entry.id" type="button" @click="emit('select', entry)">
            <i class="status" :class="tone(entry.outcome)" />
            <span><strong>{{ entry.name }}</strong><small>{{ entrySummary(entry) }}</small></span>
            <em>{{ entry.phase }}</em>
          </button>
          <details v-if="category.entries.length > 3" class="more-items">
            <summary>Show {{ category.entries.length - 3 }} more</summary>
            <button v-for="entry in category.entries.slice(3)" :key="entry.id" type="button" @click="emit('select', entry)">
              <i class="status" :class="tone(entry.outcome)" /><span><strong>{{ entry.name }}</strong><small>{{ entrySummary(entry) }}</small></span><em>{{ entry.phase }}</em>
            </button>
          </details>
        </div>
      </article>
    </div>
  </section>
</template>
