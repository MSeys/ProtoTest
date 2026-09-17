<script setup lang="ts">
import { computed, ref } from "vue";
import type { TestTrace } from "../model/trace-schema";
import { formatDuration, milliseconds, pad, testDisplayName, testGroupName, tone } from "../model/trace-format";
import TextInput from "./TextInput.vue";
import FilterChip from "./FilterChip.vue";
import EmptyState from "./EmptyState.vue";

const props = defineProps<{ tests: TestTrace[]; selected?: TestTrace }>();
const emit = defineEmits<{ select: [test: TestTrace] }>();
const query = ref("");
const failedOnly = ref(false);

const problems = computed(() => props.tests.filter(test => test.outcome !== "Succeeded" && test.outcome !== "Skipped").length);

/** The number beside a test is the one the run screen gave it, so the two views agree. */
const numbered = computed(() => [...props.tests]
  .sort((left, right) => Date.parse(left.startedAtUtc) - Date.parse(right.startedAtUtc))
  .map((test, index) => ({ test, index: index + 1 })));

const groups = computed(() => {
  const text = query.value.toLocaleLowerCase();
  const visible = numbered.value.filter(({ test }) =>
    (!failedOnly.value || (test.outcome !== "Succeeded" && test.outcome !== "Skipped"))
    && (!text || testDisplayName(test).toLocaleLowerCase().includes(text)
      || (test.className ?? "").toLocaleLowerCase().includes(text)));
  const byGroup = new Map<string, typeof visible>();
  for (const item of visible) {
    const name = testGroupName(item.test);
    byGroup.set(name, [...(byGroup.get(name) ?? []), item]);
  }
  return [...byGroup.entries()];
});
</script>

<template>
  <aside class="rail" aria-label="Tests in this run">
    <div class="rail-head">
      <TextInput v-model="query" type="search" placeholder="Find a test" label="Find a test" />
      <FilterChip label="Needs attention" :count="problems" :tone="problems ? 'danger' : 'neutral'"
                  :active="failedOnly" @select="failedOnly = !failedOnly" />
    </div>
    <div class="rail-list">
      <section v-for="[group, items] in groups" :key="group">
        <h3>{{ group }}</h3>
        <button v-for="{ test, index } in items" :key="test.testId" type="button"
                class="rail-row" :class="[tone(test.outcome), { active: test.testId === selected?.testId }]"
                :title="`${testDisplayName(test)} · ${test.outcome}`" @click="emit('select', test)">
          <b class="mono">{{ pad(index) }}</b>
          <span>{{ testDisplayName(test) }}</span>
          <small class="mono">{{ formatDuration(milliseconds(test.duration)) }}</small>
          <i class="status" :class="tone(test.outcome)" />
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
.rail-head { padding: var(--space-2); display: grid; gap: var(--space-2); justify-items: start; border-bottom: 1px solid var(--border); }
.rail-head > :first-child { width: 100%; }
.rail-list { min-height: 0; padding: var(--space-2); overflow: auto; }
.rail-list h3 {
  position: sticky;
  top: calc(var(--space-2) * -1);
  z-index: 1;
  padding: var(--space-2) var(--space-2) var(--space-1);
  color: var(--dim);
  font-family: var(--font-ui);
  font-size: var(--text-micro);
  letter-spacing: .08em;
  text-transform: uppercase;
  background: var(--surface);
}
.rail-row {
  width: 100%;
  min-height: var(--row-height);
  padding: 0 var(--space-2);
  display: grid;
  grid-template-columns: 22px minmax(0, 1fr) auto 7px;
  align-items: center;
  gap: var(--space-2);
  border: 1px solid transparent;
  border-radius: var(--radius-chip);
  background: transparent;
  text-align: left;
}
.rail-row:hover { background: var(--hover); }
.rail-row.active { border-color: var(--blueprint); background: var(--blueprint-soft); }
.rail-row b { color: var(--dim); font-size: var(--text-micro); font-weight: var(--weight-regular); }
.rail-row span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: var(--text-meta); }
.rail-row small { color: var(--muted); font-size: var(--text-micro); }
</style>
