<script setup lang="ts">
import { computed, ref } from "vue";
import type { Run, TestTrace } from "../trace/model";
import { formatDate, formatDuration, pad, phaseSegments, testCodeName, testGroup, testMatches, testTitle, tone } from "../trace/format";
import Panel from "../ui/Panel.vue";
import TextInput from "../ui/TextInput.vue";
import FilterChip from "../ui/FilterChip.vue";
import EmptyState from "../ui/EmptyState.vue";
import VisibilityStrip from "../ui/VisibilityStrip.vue";

const props = defineProps<{ run: Run; fileName: string }>();
const emit = defineEmits<{ select: [test: TestTrace] }>();

const query = ref("");
const filter = ref<"all" | "attention">("all");

const needsAttention = (test: TestTrace) => test.outcome !== "succeeded" && test.outcome !== "skipped";
const attention = computed(() => props.run.tests.filter(needsAttention)
  .sort((left, right) => (left.outcome === "failed" ? 0 : 1) - (right.outcome === "failed" ? 0 : 1) || left.number - right.number));

/** The run in one sentence: what went wrong first, then what passed. */
const verdict = computed(() => {
  const counts = props.run.counts;
  const parts: { text: string; tone: string }[] = [];
  if (counts.failed) parts.push({ text: `${counts.failed} failed`, tone: "danger" });
  if (counts.partial) parts.push({ text: `${counts.partial} partial`, tone: "warning" });
  if (counts.cancelled) parts.push({ text: `${counts.cancelled} cancelled`, tone: "warning" });
  parts.push({ text: `${counts.succeeded} passed`, tone: "success" });
  if (counts.skipped) parts.push({ text: `${counts.skipped} skipped`, tone: "neutral" });
  return parts;
});

const environment = computed(() => [props.run.environment.runtime, props.run.environment.os].filter(Boolean).join(" on "));

const visible = computed(() => props.run.tests.filter(test =>
  (filter.value === "all" || needsAttention(test)) && testMatches(test, query.value)));

/** Each test's phases placed on the run's own time axis, so parallel and slow tests show as such. */
function bars(test: TestTrace) {
  const total = Math.max(props.run.duration, 1);
  return phaseSegments(test).map(segment => ({
    phase: segment.phase,
    left: Math.max(0, ((segment.start - props.run.start) / total) * 100),
    width: Math.max(0.4, (segment.duration / total) * 100)
  }));
}

/** Why a test needs attention, in the words of the check that decided it. */
function reason(test: TestTrace): { title: string; detail: string } {
  const failure = test.failure;
  if (!failure) return { title: test.outcome === "partial" ? "Finished with a partial result" : "No failing operation recorded", detail: "" };
  const mismatch = failure.mismatches[0];
  const detail = mismatch
    ? `${mismatch.path}: expected ${JSON.stringify(mismatch.expected)}, got ${JSON.stringify(mismatch.actual)}${failure.mismatches.length > 1 ? `, and ${failure.mismatches.length - 1} more` : ""}`
    : failure.span.error?.message.split(/\r?\n/)[0] ?? failure.check?.detail ?? "";
  return { title: failure.span.name, detail };
}
</script>

<template>
  <div class="run">
    <header class="summary">
      <div class="headline">
        <h1>
          <template v-for="(part, index) in verdict" :key="part.text">
            <span :class="part.tone">{{ part.text }}</span><span v-if="index < verdict.length - 1" class="sep">, </span>
          </template>
        </h1>
        <p class="meta">
          <span>{{ run.tests.length }} tests in {{ formatDuration(run.duration) }}</span>
          <span>{{ formatDate(run.start) }}</span>
          <span v-if="environment">{{ environment }}</span>
          <span class="file">{{ fileName }}</span>
        </p>
      </div>
      <!-- The run's shape at a glance: one segment per test, in the order they started, coloured by outcome. -->
      <div class="strip" role="img" :aria-label="verdict.map(part => part.text).join(', ')">
        <button v-for="test in run.tests" :key="test.id" type="button" class="tick" :class="tone(test.outcome)"
                :title="`${pad(test.number)} ${testTitle(test)}`" @click="emit('select', test)" />
      </div>
    </header>

    <VisibilityStrip :visibility="run.visibility" />

    <Panel v-if="attention.length || run.findings.length || run.gates.length" title="Needs attention"
           subtitle="Failing and partial tests first, then what the run itself found and how its gates judged it." pad="none">
      <div class="attention">
        <button v-for="test in attention" :key="test.id" type="button" class="issue" :class="tone(test.outcome)" @click="emit('select', test)">
          <b>{{ pad(test.number) }}</b>
          <span class="issue-main">
            <strong>{{ testTitle(test) }}</strong>
            <span class="issue-reason">{{ reason(test).title }}</span>
            <span v-if="reason(test).detail" class="issue-detail">{{ reason(test).detail }}</span>
          </span>
          <span class="issue-kind">{{ test.outcome === "failed" ? "Failed" : "Partial" }}</span>
        </button>
        <component :is="item.test ? 'button' : 'div'" v-for="(item, index) in run.findings" :key="`finding-${index}`"
                   :type="item.test ? 'button' : undefined" class="issue finding" :class="item.finding.status.toLowerCase()"
                   @click="item.test && emit('select', item.test)">
          <b>{{ item.test ? pad(item.test.number) : "Run" }}</b>
          <span class="issue-main">
            <strong>{{ item.finding.message }}</strong>
            <span class="issue-reason">{{ [item.finding.category, ...item.finding.tags].filter(Boolean).join(", ") }}</span>
          </span>
          <span class="issue-kind">{{ item.finding.status }} finding</span>
        </component>
        <div v-for="gate in run.gates" :key="gate.name" class="issue gate" :class="tone(gate.outcome)">
          <b>Gate</b>
          <span class="issue-main">
            <strong>{{ gate.name }}</strong>
            <span v-if="gate.message" class="issue-reason">{{ gate.message }}</span>
          </span>
          <span class="issue-kind">{{ gate.status }}</span>
        </div>
      </div>
    </Panel>

    <Panel title="Tests" subtitle="In the order they started. The bar is where the test ran within the run, split by phase." pad="none">
      <template #actions>
        <div class="filters">
          <FilterChip label="All" :count="run.tests.length" :active="filter === 'all'" @select="filter = 'all'" />
          <FilterChip label="Needs attention" :count="attention.length" :tone="attention.length ? 'danger' : 'neutral'"
                      :active="filter === 'attention'" @select="filter = 'attention'" />
          <TextInput v-model="query" type="search" placeholder="Find a test" label="Find a test" class="find" />
        </div>
      </template>
      <div class="legend" aria-hidden="true">
        <span v-for="phase in ['setup', 'execution', 'rollback', 'teardown']" :key="phase"><i :style="{ background: `var(--phase-${phase})` }" />{{ phase }}</span>
      </div>
      <div v-if="visible.length" class="tests">
        <button v-for="test in visible" :key="test.id" type="button" class="test-row" :class="tone(test.outcome)"
                :title="testCodeName(test)" @click="emit('select', test)">
          <b>{{ pad(test.number) }}</b>
          <span class="test-name">
            <span>{{ testTitle(test) }}</span>
            <small>{{ testGroup(test) }}</small>
          </span>
          <span class="bar">
            <i v-for="segment in bars(test)" :key="segment.phase"
               :style="{ left: `${segment.left}%`, width: `${segment.width}%`, background: `var(--phase-${segment.phase})` }" />
          </span>
          <small class="duration">{{ formatDuration(test.duration) }}</small>
          <i class="status" :class="tone(test.outcome)" />
        </button>
      </div>
      <EmptyState v-else message="No test matches this filter.">
        <FilterChip label="Show all tests" @select="filter = 'all'; query = ''" />
      </EmptyState>
    </Panel>
  </div>
</template>

<style scoped>
.run { display: grid; gap: var(--space-4); container-type: inline-size; }

.summary {
  padding: var(--space-4) var(--space-1) 0;
  display: grid;
  gap: var(--space-3);
}
.headline { display: grid; gap: var(--space-1); }
.headline h1 { font-size: var(--text-display); letter-spacing: var(--tracking-display); line-height: var(--leading-tight); }
.headline h1 .danger { color: var(--danger); }
.headline h1 .warning { color: var(--warning); }
.headline h1 .success { color: var(--text); }
.headline h1 .sep { color: var(--dim); }
.meta { display: flex; flex-wrap: wrap; gap: var(--space-1) var(--space-4); color: var(--muted); font-size: var(--text-meta); }
.file { color: var(--dim); }

/* The one bold element on this screen: every test as a tick, so the run's outcome has a shape. */
.strip { display: flex; align-items: flex-end; gap: 2px; height: 12px; }
/* Passing tests are the quiet majority; what did not pass stands up out of the line. */
.tick { flex: 1 1 0; min-width: 3px; height: 5px; padding: 0; border: 0; border-radius: var(--radius-hairline); background: var(--outcome-succeeded); opacity: .45; }
.tick:hover { opacity: 1; }
.tick.danger, .tick.warning { height: 12px; }
.tick.danger { background: var(--outcome-failed); opacity: 1; }
.tick.warning { background: var(--outcome-partial); opacity: 1; }
.tick.neutral { background: var(--outcome-unknown); }

.attention { display: grid; }
.issue {
  width: 100%;
  padding: var(--space-3) var(--space-4);
  display: grid;
  grid-template-columns: 34px minmax(0, 1fr) auto;
  align-items: start;
  gap: var(--space-3);
  border: 0;
  border-left: 2px solid transparent;
  background: transparent;
  text-align: left;
}
.issue + .issue { border-top: 1px solid var(--border); }
button.issue:hover { background: var(--hover); }
.issue.danger { border-left-color: var(--danger); }
.issue.warning { border-left-color: var(--warning); }
.issue.success { border-left-color: var(--success); }
.issue b { padding-top: 1px; color: var(--dim); font: var(--text-micro) var(--font-mono); }
.issue-main { min-width: 0; display: grid; gap: 2px; }
.issue-main strong { font-size: var(--text-body); }
.issue-reason { color: var(--muted); font-size: var(--text-meta); }
.issue-detail { overflow-wrap: anywhere; color: var(--text); font: var(--text-micro)/var(--leading) var(--font-mono); }
.issue-kind { color: var(--muted); font-size: var(--text-micro); white-space: nowrap; }
.issue.danger .issue-kind { color: var(--danger); }
.issue.warning .issue-kind { color: var(--warning); }

.filters { display: flex; flex-wrap: wrap; align-items: center; justify-content: flex-end; gap: var(--space-2); }
.find { width: clamp(140px, 24cqi, 240px); }
.legend { padding: var(--space-2) var(--space-4) 0; display: flex; flex-wrap: wrap; gap: var(--space-4); color: var(--muted); font-size: var(--text-micro); text-transform: capitalize; }
.legend span { display: inline-flex; align-items: center; gap: var(--space-1); }
.legend i { width: 10px; height: 4px; border-radius: var(--radius-hairline); }

.tests { padding: var(--space-2) var(--space-2) var(--space-3); display: grid; }
.test-row {
  width: 100%;
  min-height: var(--row-height);
  padding: var(--space-1) var(--space-2);
  display: grid;
  grid-template-columns: 22px minmax(0, 1.1fr) minmax(0, 1fr) 58px 7px;
  align-items: center;
  gap: var(--space-3);
  border: 0;
  border-radius: var(--radius-chip);
  background: transparent;
  text-align: left;
}
.test-row:hover { background: var(--hover); }
.test-row b { color: var(--dim); font: var(--text-micro) var(--font-mono); }
.test-row.danger b { color: var(--danger); }
.test-row.warning b { color: var(--warning); }
.test-name { min-width: 0; display: flex; align-items: baseline; gap: var(--space-2); }
.test-name span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: var(--text-meta); }
.test-name small { flex: none; color: var(--dim); font-size: var(--text-micro); }
.bar { position: relative; height: 6px; border-radius: var(--radius-hairline); background: var(--surface-2); }
.bar i { position: absolute; top: 0; bottom: 0; border-radius: var(--radius-hairline); }
.duration { color: var(--muted); font-size: var(--text-micro); text-align: right; font-variant-numeric: tabular-nums; }

@container (max-width: 640px) {
  .test-row { grid-template-columns: 22px minmax(0, 1fr) 52px 7px; }
  .bar, .test-name small { display: none; }
  .issue { grid-template-columns: 30px minmax(0, 1fr); }
  .issue-kind { grid-column: 2; }
}
</style>
