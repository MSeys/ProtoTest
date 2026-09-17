<script setup lang="ts">
import { computed, ref } from "vue";
import type { TestTrace, TraceArtifact, TraceEntry, TraceRun } from "../model/trace-schema";
import { entryTitle, formatDate, formatDuration, milliseconds, testDisplayName, testGroupName, tone } from "../model/trace-format";
import { nodeType } from "../model/trace-levels";
import { primaryFailure, runDuration, testSegments } from "../model/trace-rows";
import Panel from "../ui/Panel.vue";
import MetricTile from "../ui/MetricTile.vue";
import TimelineRow from "../ui/TimelineRow.vue";
import FilterChip from "../ui/FilterChip.vue";
import TextInput from "../ui/TextInput.vue";
import KindChip from "../ui/KindChip.vue";
import PhaseKey from "../ui/PhaseKey.vue";
import EmptyState from "../ui/EmptyState.vue";

const props = defineProps<{ run: TraceRun; fileName: string }>();
const filter = ref<"all" | "failed" | "partial" | "passed">("all");
const query = ref("");
const emit = defineEmits<{ select: [test: TestTrace]; artifact: [artifact: TraceArtifact] }>();

const passed = computed(() => props.run.tests.filter(test => test.outcome === "Succeeded").length);
const partial = computed(() => props.run.tests.filter(test => test.outcome === "Partial").length);
const failed = computed(() => props.run.tests.filter(test => test.outcome === "Failed").length);
const entryCount = computed(() => props.run.tests.reduce((sum, test) => sum + test.entries.length, 0));
const artifactCount = computed(() => (props.run.artifacts?.length ?? 0) + props.run.tests.reduce((sum, test) => sum + (test.artifacts?.length ?? 0), 0));
const runEntries = computed(() => props.run.entries ?? []);

interface StatusItem {
  id: string;
  outcome: TestTrace["outcome"];
  type: { id: string; label: string };
  label: string;
  /** The operation that explains the outcome, in the trace's own words. */
  cause?: string;
  detail: string;
  test?: TestTrace;
}

function excerpt(value: string, length = 160): string {
  return value.length > length ? `${value.slice(0, length)}…` : value;
}

/**
 * What needs attention, most severe first. A test that failed or went partial is shown with the
 * operation that caused it, so the run screen answers "what is wrong" without opening the test.
 */
const status = computed(() => {
  const items: StatusItem[] = [];
  for (const test of props.run.tests) {
    if (test.outcome === "Succeeded" || test.outcome === "Skipped" || test.outcome === "Unknown") continue;
    const failure = primaryFailure(test);
    items.push({
      id: test.testId,
      outcome: test.outcome,
      type: failure ? nodeType(failure) : { id: "lifecycle", label: "Test" },
      label: testDisplayName(test),
      cause: failure ? entryTitle(failure) : undefined,
      detail: excerpt(failure?.error?.message ?? test.error?.message ?? `${testGroupName(test)} · ${test.outcome}`),
      test
    });
  }
  for (const test of props.run.tests) {
    for (const entry of test.entries) {
      if (entry.kind !== "finding.record") continue;
      items.push(findingItem(entry, testDisplayName(test), test));
    }
  }
  for (const entry of runEntries.value) {
    if (entry.kind !== "gate.evaluate" && entry.kind !== "finding.record") continue;
    items.push(findingItem(entry, entry.kind));
  }
  return items.sort((left, right) => severity(right.outcome) - severity(left.outcome));
});

function findingItem(entry: TraceEntry, fallback: string, test?: TestTrace): StatusItem {
  const attributes = entry.attributes ?? {};
  return {
    id: entry.id,
    outcome: entry.outcome,
    type: nodeType(entry),
    label: entryTitle(entry),
    cause: attributes["finding.status"] ?? attributes["gate.status"] ?? undefined,
    detail: excerpt(attributes["finding.message"] ?? attributes["gate.message"] ?? fallback),
    test
  };
}

function severity(outcome: TestTrace["outcome"]): number {
  return outcome === "Failed" ? 3 : outcome === "Partial" || outcome === "Cancelled" ? 2 : 1;
}

const counts = computed(() => ({
  all: props.run.tests.length,
  failed: failed.value,
  partial: partial.value,
  passed: passed.value
}));
const filters = [
  { id: "all", label: "All", tone: "neutral" },
  { id: "failed", label: "Failed", tone: "danger" },
  { id: "partial", label: "Partial", tone: "warning" },
  { id: "passed", label: "Passed", tone: "success" }
] as const;

const visibleTests = computed(() => props.run.tests.filter(test =>
  (filter.value === "all" || (filter.value === "failed" ? test.outcome === "Failed" : filter.value === "partial" ? test.outcome === "Partial" : test.outcome === "Succeeded"))
  && (!query.value || testDisplayName(test).toLocaleLowerCase().includes(query.value.toLocaleLowerCase())
    || (test.className ?? "").toLocaleLowerCase().includes(query.value.toLocaleLowerCase()))));

const bounds = computed(() => {
  const stamps: number[] = [];
  for (const test of props.run.tests) {
    stamps.push(Date.parse(test.startedAtUtc), Date.parse(test.startedAtUtc) + milliseconds(test.duration));
  }
  for (const entry of runEntries.value) {
    stamps.push(Date.parse(entry.timestampUtc), Date.parse(entry.timestampUtc) + milliseconds(entry.duration));
  }
  if (props.run.completedAtUtc) stamps.push(Date.parse(props.run.completedAtUtc));
  if (!stamps.length) return { start: 0, duration: 1 };
  const start = Math.min(...stamps);
  return { start, duration: Math.max(Math.max(...stamps) - start, 1) };
});

const rows = computed(() => [...visibleTests.value]
  .sort((left, right) => Date.parse(left.startedAtUtc) - Date.parse(right.startedAtUtc))
  .map((test, index) => ({
    test,
    index: index + 1,
    left: ((Date.parse(test.startedAtUtc) - bounds.value.start) / bounds.value.duration) * 100,
    width: Math.max((milliseconds(test.duration) / bounds.value.duration) * 100, .6),
    segments: testSegments(test)
  })));

function artifactKind(artifact: TraceArtifact): string {
  return artifact.mediaType.includes("html") ? "HTML" : artifact.mediaType.includes("json") ? "JSON" : "FILE";
}
</script>

<template>
  <div class="run-view">
    <header class="run-heading">
      <div class="identity">
        <span class="eyebrow">Test run</span>
        <h1>{{ fileName }}</h1>
        <p class="mono">{{ formatDate(run.startedAtUtc) }} · RUN {{ run.runId.slice(0, 8).toUpperCase() }}</p>
        <ul v-if="run.environment" class="environment">
          <li>{{ run.environment.runtime }}</li>
          <li>{{ run.environment.os }}</li>
          <li>{{ run.environment.processArchitecture }}</li>
        </ul>
      </div>
      <div class="metrics">
        <MetricTile label="Tests" :value="run.tests.length" />
        <MetricTile label="Passed" :value="passed" tone="success" />
        <MetricTile label="Partial" :value="partial" :tone="partial ? 'warning' : 'neutral'" />
        <MetricTile label="Failed" :value="failed" :tone="failed ? 'danger' : 'neutral'" />
        <MetricTile label="Trace entries" :value="entryCount" />
        <MetricTile label="Artifacts" :value="artifactCount" />
        <MetricTile label="Duration" :value="formatDuration(runDuration(run.tests))" />
      </div>
    </header>

    <Panel title="Run status" subtitle="What needs attention, then how the run judged itself" pad="tight">
      <template #actions><span class="count">{{ status.length }}</span></template>
      <p v-if="!status.length" class="clear"><i class="status success" />Every test passed and nothing was reported.</p>
      <div v-else class="status-list">
        <button v-for="item in status" :key="item.id" type="button" class="status-row" :class="tone(item.outcome)"
                :title="item.detail" @click="item.test && emit('select', item.test)">
          <i class="status" :class="tone(item.outcome)" />
          <span class="status-body">
            <strong>{{ item.label }}</strong>
            <small><b v-if="item.cause" class="mono">{{ item.cause }}</b>{{ item.detail }}</small>
          </span>
          <KindChip :type="item.type" />
        </button>
      </div>
    </Panel>

    <Panel title="Tests" subtitle="One row per test, in start order; the bar is the test, split by phase" pad="tight">
      <template #actions>
        <PhaseKey class="legend" />
        <TextInput v-model="query" type="search" placeholder="Filter tests" label="Filter tests" class="search" />
        <div class="chips">
          <FilterChip v-for="item in filters" :key="item.id" :label="item.label" :count="counts[item.id]"
                      :tone="item.tone" :active="filter === item.id" @select="filter = item.id" />
        </div>
      </template>
      <div v-if="rows.length" class="rows">
        <TimelineRow v-for="row in rows" :key="row.test.testId"
                     :index="row.index" :name="testDisplayName(row.test)" :group="testGroupName(row.test)"
                     :outcome="row.test.outcome" :duration="milliseconds(row.test.duration)"
                     :offset="row.left" :width="row.width" :segments="row.segments"
                     @select="emit('select', row.test)" />
      </div>
      <EmptyState v-else message="No test matches this filter." />
    </Panel>

    <Panel v-if="run.artifacts?.length" title="Run outputs" subtitle="Generated by report sinks and bundled inside this trace" pad="tight">
      <template #actions><span class="count">{{ run.artifacts.length }}</span></template>
      <div class="outputs">
        <button v-for="artifact in run.artifacts" :key="artifact.id" type="button" class="output" @click="emit('artifact', artifact)">
          <span class="output-kind mono">{{ artifactKind(artifact) }}</span>
          <span class="output-body">
            <strong>{{ artifact.name }}</strong>
            <small>{{ artifact.description || artifact.mediaType }}</small>
          </span>
        </button>
      </div>
    </Panel>
  </div>
</template>

<style scoped>
.run-view { display: grid; gap: var(--space-4); align-content: start; }
.run-heading { display: flex; flex-wrap: wrap; align-items: flex-end; justify-content: space-between; gap: var(--space-5); }
.identity { min-width: 0; }
.run-heading h1 { margin: var(--space-1) 0; font-size: var(--text-display); letter-spacing: var(--tracking-display); }
.run-heading p { color: var(--muted); font-size: var(--text-body); }
.environment { margin: var(--space-2) 0 0; padding: 0; display: flex; flex-wrap: wrap; gap: var(--space-2); list-style: none; }
.environment li { padding: 2px var(--space-2); border: 1px solid var(--border); border-radius: var(--radius-chip); color: var(--muted); font: var(--text-micro) var(--font-mono); }
.metrics { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: var(--space-2); }
.count { color: var(--muted); font: var(--text-micro) var(--font-mono); }

.status-list { display: grid; gap: var(--space-2); }
.status-row {
  padding: var(--space-3) var(--space-4);
  display: grid;
  grid-template-columns: 7px minmax(0, 1fr) auto;
  align-items: center;
  gap: var(--space-3);
  border: 1px solid var(--border);
  border-left-width: 3px;
  border-radius: var(--radius-control);
  background: var(--surface-2);
  text-align: left;
}
.status-row:hover { border-color: var(--border-strong); background: var(--hover); }
.status-row.danger { border-left-color: var(--danger); }
.status-row.warning { border-left-color: var(--warning); }
.status-row.success { border-left-color: var(--success); }
.status-body { min-width: 0; display: flex; flex-direction: column; gap: 1px; }
.status-body strong { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: var(--text-body); }
.status-body small { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--muted); font-size: var(--text-micro); }
/* The operation that caused the outcome leads the line, in mono, because it is data. */
.status-body small b { margin-right: var(--space-2); color: var(--text); font-weight: var(--weight-semibold); }
.clear {
  padding: var(--space-3) var(--space-4);
  display: flex;
  align-items: center;
  gap: var(--space-3);
  border: 1px solid var(--border);
  border-left: 3px solid var(--success);
  border-radius: var(--radius-control);
  background: var(--surface-2);
  color: var(--muted);
  font-size: var(--text-meta);
}

.legend { margin-right: auto; }
.search { width: clamp(140px, 16vw, 220px); }
.chips { display: flex; flex-wrap: wrap; gap: var(--space-1); }
.rows { display: grid; gap: 2px; }

.outputs { display: flex; flex-wrap: wrap; gap: var(--space-2); }
.output {
  min-width: 240px;
  flex: 1 1 280px;
  padding: var(--space-3);
  display: grid;
  grid-template-columns: auto minmax(0, 1fr);
  align-items: center;
  gap: var(--space-3);
  border: 1px solid var(--border);
  border-radius: var(--radius-control);
  background: var(--surface-2);
  text-align: left;
}
.output:hover { border-color: var(--blueprint); background: var(--hover); }
.output-kind { padding: 1px var(--space-2); border: 1px solid var(--border-strong); border-radius: var(--radius-chip); color: var(--muted); font-size: var(--text-micro); }
.output-body { min-width: 0; display: flex; flex-direction: column; }
.output-body strong, .output-body small { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.output-body strong { font-size: var(--text-body); }
.output-body small { color: var(--muted); font-size: var(--text-micro); }
</style>
