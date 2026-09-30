<script setup lang="ts">
import { computed } from "vue";
import type { Outcome, TestTrace } from "../trace/model";
import { failureReason, formatDuration, needsAttention, outcomeLabel, pad, testCodeName, testTitle, tone, verdictParts } from "../trace/format";
import { groupedTests, resetTestFilter, setTestOrder, testFilter } from "./testFilter";
import TextInput from "./TextInput.vue";
import FilterChip from "./FilterChip.vue";
import OutcomeFilters from "./OutcomeFilters.vue";
import EmptyState from "./EmptyState.vue";

/*
 * The one list of the run's tests, on every screen, with the run itself as its first row. Each row is a
 * link to its test, so a test opens in a new tab like any other page; a plain click stays in place.
 */
const props = defineProps<{
  tests: TestTrace[];
  counts: Record<Outcome, number>;
  selected?: TestTrace;
  /** The run screen is open: the run's row is the current one. */
  atRun: boolean;
  /** Numbers only, beside open details; the list keeps its place and its filter. */
  slim?: boolean;
  hrefFor: (test: TestTrace) => string;
}>();
const emit = defineEmits<{ select: [test: TestTrace]; run: []; expand: [] }>();

const groups = computed(() => groupedTests(props.tests));
const verdict = computed(() => verdictParts(props.counts));

/** A test that did not pass says why in the list, so the reader can pick the right one without opening it. */
function reason(test: TestTrace): string {
  // Without a failing operation the outcome is still stated in words: never colour alone.
  if (!needsAttention(test)) return "";
  if (!test.failure) return outcomeLabel(test.outcome);
  return failureReason(test).title;
}

/** A plain click opens in place; a modified one is the browser's (a new tab, a copied link). */
function pick(event: MouseEvent, then: () => void) {
  if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
  event.preventDefault();
  then();
}
</script>

<template>
  <aside class="rail" :class="{ slim }" aria-label="Tests in this run">
    <div v-if="!slim" class="rail-head">
      <TextInput v-model="testFilter.query" type="search" placeholder="Find a test" label="Find a test" />
      <OutcomeFilters :tests="tests" />
      <div class="order" role="group" aria-label="Order the tests">
        <span>Group</span>
        <button type="button" :aria-pressed="testFilter.order === 'class'" @click="setTestOrder('class')">By class</button>
        <button type="button" :aria-pressed="testFilter.order === 'run'" @click="setTestOrder('run')">In run order</button>
      </div>
    </div>
    <nav class="rail-list" aria-label="Run and tests">
      <a class="run-row" :class="{ active: atRun }" href="#/" :aria-current="atRun ? 'page' : undefined"
         :title="slim ? verdict.map(part => part.text).join(', ') : undefined" @click="pick($event, () => emit('run'))">
        <strong>Run</strong>
        <!-- Two lines on screen, one phrase when read out: "Run, 4 failed, 14 passed". -->
        <span v-if="!slim" class="visually-hidden">, </span>
        <span v-if="!slim" class="verdict">
          <template v-for="(part, index) in verdict" :key="part.text"><span :class="part.tone">{{ part.text }}</span><template v-if="index < verdict.length - 1">, </template></template>
        </span>
      </a>
      <section v-for="[group, entries] in groups" :key="group">
        <h3 v-if="group && !slim">{{ group }}</h3>
        <a v-for="test in entries" :key="test.id" class="rail-row" :class="[tone(test.outcome), { active: test.id === selected?.id }]"
           :href="hrefFor(test)" :title="slim ? `${pad(test.number)} ${testTitle(test)}` : testCodeName(test)"
           :aria-current="test.id === selected?.id ? 'page' : undefined" @click="pick($event, () => emit('select', test))">
          <b>{{ pad(test.number) }}</b>
          <i class="status" :class="tone(test.outcome)" aria-hidden="true" />
          <template v-if="!slim">
            <span class="name">{{ testTitle(test) }}</span>
            <small>{{ formatDuration(test.duration) }}</small>
          </template>
          <span class="visually-hidden">{{ outcomeLabel(test.outcome) }}</span>
          <span v-if="!slim && reason(test)" class="reason">{{ reason(test) }}</span>
        </a>
      </section>
      <EmptyState v-if="!groups.length && !slim" message="No test matches this filter.">
        <FilterChip label="Show all tests" @select="resetTestFilter()" />
      </EmptyState>
    </nav>
    <button v-if="slim" type="button" class="expand" aria-label="Show the whole test list" title="Show the whole test list" @click="emit('expand')">
      <svg viewBox="0 0 10 10" width="10" height="10" aria-hidden="true"><path d="M3.6 2 6.6 5 3.6 8" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" /></svg>
    </button>
  </aside>
</template>

<style scoped>
.rail {
  min-height: 0;
  display: grid;
  grid-template-rows: auto minmax(0, 1fr) auto;
  border: 1px solid var(--border);
  border-radius: var(--radius-panel);
  background: var(--surface);
  overflow: hidden;
}
.rail-head { padding: var(--space-3); display: grid; gap: var(--space-2); border-bottom: 1px solid var(--border); }
.order { display: flex; flex-wrap: wrap; align-items: center; gap: var(--space-1); color: var(--dim); font-size: var(--text-meta); }
.order span { margin-right: var(--space-1); }
.order button { padding: 2px var(--space-2); border: 0; border-radius: var(--radius-chip); background: transparent; color: var(--muted); font-size: var(--text-meta); }
.order button:hover { color: var(--text); }
.order button[aria-pressed="true"] { background: var(--surface-2); color: var(--text); font-weight: var(--weight-semibold); }
.rail-list { min-height: 0; padding: var(--space-2); overflow: auto; overscroll-behavior: contain; }
.rail-list h3 {
  position: sticky;
  top: calc(var(--space-2) * -1);
  z-index: 1;
  padding: var(--space-4) var(--space-2) var(--space-1);
  background: var(--surface);
  color: var(--muted);
  font-family: var(--font-ui);
  font-size: var(--text-meta);
  font-weight: var(--weight-semibold);
  letter-spacing: 0;
}
a { color: inherit; text-decoration: none; }
/* The run is the list's first entry: the same place, the same kind of link, one level up. */
.run-row {
  margin-bottom: var(--space-1);
  padding: var(--space-2) var(--space-3);
  display: grid;
  gap: 2px;
  border: 1px solid var(--border);
  border-radius: var(--radius-control);
  background: var(--surface-2);
}
.run-row:hover { border-color: var(--border-strong); }
.run-row.active { border-color: var(--blueprint); background: var(--blueprint-soft); }
.run-row strong { font-size: var(--text-strong); }
.verdict { color: var(--muted); font-size: var(--text-meta); }
.verdict .danger { color: var(--danger); font-weight: var(--weight-semibold); }
.verdict .warning { color: var(--warning); font-weight: var(--weight-semibold); }
.rail-row {
  min-height: var(--control-height);
  padding: var(--space-1) var(--space-2);
  display: grid;
  grid-template-columns: 22px 7px minmax(0, 1fr) auto;
  align-items: center;
  gap: 0 var(--space-2);
  border: 1px solid transparent;
  border-radius: var(--radius-chip);
  /* Rows are uniform and independent, so one row's change never re-lays-out the list. */
  contain: layout paint;
}
.rail-row:hover { background: var(--hover); }
.rail-row.active { border-color: var(--blueprint); background: var(--blueprint-soft); }
/* The number is the one the run list and the test header use, so they can be read against each other. */
.rail-row b { color: var(--dim); font: var(--text-meta) var(--font-mono); }
.rail-row.danger b { color: var(--danger); }
.rail-row.warning b { color: var(--warning); }
.rail-row .name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: var(--text-body); }
.rail-row small { color: var(--muted); font-size: var(--text-meta); font-variant-numeric: tabular-nums; }
.reason { grid-column: 3 / -1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: var(--text-meta); }
/* The dot and the number already say it failed; the reason reads as information, not a second alarm. */
.rail-row .reason { color: var(--muted); }
/* On the selected row the tint sits under the text, so the failure red deepens a step to hold its contrast. */
html[data-theme="light"] .rail-row.active.danger .reason { color: color-mix(in srgb, var(--danger) 85%, var(--text)); }
.visually-hidden { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); }

/* Slim: the numbers and their outcome, one column, so open details get the room. */
.slim .rail-list { padding: var(--space-1); }
.slim .run-row { padding: var(--space-2) 0; justify-items: center; }
.slim .run-row strong { font-size: var(--text-meta); }
.slim .rail-row { padding: var(--space-1) 0; grid-template-columns: 1fr; justify-items: center; gap: 2px; }
.expand {
  height: var(--control-height);
  display: grid;
  place-items: center;
  border: 0;
  border-top: 1px solid var(--border);
  background: transparent;
  color: var(--muted);
}
.expand:hover { background: var(--hover); color: var(--text); }
</style>
