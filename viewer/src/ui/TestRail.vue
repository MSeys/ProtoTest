<script setup lang="ts">
import { computed, ref } from "vue";
import type { TestTrace } from "../trace/model";
import { formatDuration, pad, testCodeName, testGroup, testMatches, testTitle, tone } from "../trace/format";
import TextInput from "./TextInput.vue";
import FilterChip from "./FilterChip.vue";
import EmptyState from "./EmptyState.vue";

const props = defineProps<{ tests: TestTrace[]; selected?: TestTrace }>();
const emit = defineEmits<{ select: [test: TestTrace] }>();
const query = ref("");
const problemsOnly = ref(false);
/** Grouped by class reads as a suite; in run order the numbers count up like the run list. Remembered per reader. */
const runOrder = ref(read());
function read(): boolean {
  try { return localStorage.getItem("prototrace.rail-order") === "run"; } catch { return false; }
}
function toggleOrder() {
  runOrder.value = !runOrder.value;
  try { localStorage.setItem("prototrace.rail-order", runOrder.value ? "run" : "class"); } catch { /* storage may be disabled */ }
}

const needsAttention = (test: TestTrace) => test.outcome !== "succeeded" && test.outcome !== "skipped";
const problems = computed(() => props.tests.filter(needsAttention).length);

const groups = computed(() => {
  const byGroup = new Map<string, TestTrace[]>();
  for (const test of props.tests) {
    if (problemsOnly.value && !needsAttention(test)) continue;
    if (!testMatches(test, query.value)) continue;
    const name = runOrder.value ? "" : testGroup(test);
    byGroup.set(name, [...(byGroup.get(name) ?? []), test]);
  }
  return [...byGroup.entries()];
});

/** A test that did not pass says why in the list, so the reader can pick the right one without opening it. */
function reason(test: TestTrace): string {
  if (!needsAttention(test) || !test.failure) return "";
  return test.failure.span.name;
}
</script>

<template>
  <aside class="rail" aria-label="Tests in this run">
    <div class="rail-head">
      <TextInput v-model="query" type="search" placeholder="Find a test" label="Find a test" />
      <FilterChip label="Needs attention" :count="problems" :tone="problems ? 'danger' : 'neutral'"
                  :active="problemsOnly" @select="problemsOnly = !problemsOnly" />
      <FilterChip label="Run order" :active="runOrder" @select="toggleOrder" />
    </div>
    <div class="rail-list">
      <section v-for="[group, tests] in groups" :key="group">
        <h3 v-if="group">{{ group }}</h3>
        <button v-for="test in tests" :key="test.id" type="button"
                class="rail-row" :class="[tone(test.outcome), { active: test.id === selected?.id }]"
                :title="testCodeName(test)" @click="emit('select', test)">
          <b>{{ pad(test.number) }}</b>
          <span class="name">{{ testTitle(test) }}</span>
          <small>{{ formatDuration(test.duration) }}</small>
          <i class="status" :class="tone(test.outcome)" />
          <span v-if="reason(test)" class="reason">{{ reason(test) }}</span>
        </button>
      </section>
      <EmptyState v-if="!groups.length" message="No test matches." />
    </div>
  </aside>
</template>

<style scoped>
.rail {
  min-height: 0;
  display: grid;
  grid-template-rows: auto minmax(0, 1fr);
  border: 1px solid var(--border);
  border-radius: var(--radius-panel);
  background: var(--surface);
  overflow: hidden;
}
.rail-head { padding: var(--space-2); display: flex; flex-wrap: wrap; gap: var(--space-2); border-bottom: 1px solid var(--border); }
.rail-head > :first-child { flex: 1 1 100%; }
.rail-list { min-height: 0; padding: var(--space-2); overflow: auto; overscroll-behavior: contain; }
.rail-list h3 {
  position: sticky;
  top: calc(var(--space-2) * -1);
  z-index: 1;
  padding: var(--space-3) var(--space-2) var(--space-1);
  background: var(--surface);
  color: var(--muted);
  font-size: var(--text-micro);
  font-weight: var(--weight-semibold);
}
.rail-row {
  width: 100%;
  min-height: var(--row-height);
  padding: var(--space-1) var(--space-2);
  display: grid;
  grid-template-columns: 20px minmax(0, 1fr) auto 7px;
  align-items: center;
  gap: 0 var(--space-2);
  border: 1px solid transparent;
  border-radius: var(--radius-chip);
  background: transparent;
  text-align: left;
}
.rail-row:hover { background: var(--hover); }
.rail-row.active { border-color: var(--blueprint); background: var(--blueprint-soft); }
/* The number is the one the run list and timeline use, so the three can be read against each other. */
.rail-row b { color: var(--dim); font: var(--text-micro) var(--font-mono); }
.rail-row .name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: var(--text-meta); }
.rail-row small { color: var(--muted); font-size: var(--text-micro); font-variant-numeric: tabular-nums; }
.reason { grid-column: 2 / -1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: var(--text-micro); }
.rail-row.danger .reason { color: var(--danger); }
.rail-row.warning .reason { color: var(--warning); }
</style>
