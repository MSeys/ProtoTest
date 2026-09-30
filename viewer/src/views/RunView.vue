<script setup lang="ts">
import { computed } from "vue";
import type { Artifact, Item, Run, Span, TestTrace } from "../trace/model";
import { diagnosisRule, diagnosisRuleLabels, testFindings, untracedGaps } from "../trace/analysis";
import { failureReason, formatDate, formatDuration, formatOffset, needsAttention, outcomeLabel, pad, phaseSegments, testCodeName, testGroup, testMatches, testTitle, timelinePercent, tone } from "../trace/format";
import Panel from "../ui/Panel.vue";
import TextInput from "../ui/TextInput.vue";
import FilterChip from "../ui/FilterChip.vue";
import OutcomeFilters from "../ui/OutcomeFilters.vue";
import EmptyState from "../ui/EmptyState.vue";
import FileList from "../ui/FileList.vue";
import type { FileEntry } from "../ui/FileList.vue";
import VisibilityStrip from "../ui/VisibilityStrip.vue";
import { matchesOutcome, resetTestFilter, testFilter } from "../ui/testFilter";
import Tabs from "../ui/Tabs.vue";
import { href, type RunView } from "../router";

const props = withDefaults(defineProps<{ run: Run; fileName: string; selectedSpan?: Span; selectedItem?: Item; view?: RunView }>(), { view: "overview" });
const emit = defineEmits<{ select: [test: TestTrace, selection?: { span: string }]; span: [span: Span]; item: [item: Item]; artifact: [artifact: Artifact]; tab: [view: string] }>();

// Every file the run produced, in one place: the run's own reports first, then each test's, in test order.
// The inspector reaches an artifact through the operation that wrote it; this is the whole list.
const files = computed<FileEntry[]>(() => [
  ...[...props.run.artifacts.values()].map(artifact => ({ artifact, owner: "Run", detail: artifact.description ?? "Run artifact" })),
  ...props.run.tests.flatMap(test => [...test.artifacts.values()].map(artifact => ({
    artifact, owner: pad(test.number), detail: artifact.description ?? testTitle(test)
  })))
]);

const attention = computed(() => props.run.tests.filter(needsAttention)
  .sort((left, right) => (left.outcome === "failed" ? 0 : 1) - (right.outcome === "failed" ? 0 : 1) || left.number - right.number));

/*
 * What the run itself did wrong, outside any test: a release that failed, an error moment. Tests and
 * gates have their own rows; without these rows a broken teardown would have no surface at all.
 */
const runProblems = computed(() => ({
  spans: props.run.spans.filter(span => span.status === "failed" || span.error),
  moments: props.run.moments.filter(moment => moment.outcome === "failed" || moment.error)
}));

function problemReason(error: { message?: string } | null, fallback: string): string {
  return error?.message?.split(/\r?\n/)[0] ?? fallback;
}

/* The run owns resources - a server, a broker - that are not capabilities; the strip states what they are. */
const resources = computed(() => props.run.items.filter(item => item.kind !== "capability"));

/* A finding that names the operation it came from opens the test at that operation, keeping the deep link. */
function findingSelection(test: TestTrace | null, span: Span | null): { span: string } | undefined {
  return test && span && span.test === test ? { span: span.id } : undefined;
}

/** The run in one sentence: what went wrong first, then what passed. */
const verdict = computed(() => {
  const counts = props.run.counts;
  const parts: { text: string; tone: string }[] = [];
  if (counts.failed) parts.push({ text: `${counts.failed} failed`, tone: "danger" });
  if (counts.partial) parts.push({ text: `${counts.partial} partial`, tone: "warning" });
  if (counts.cancelled) parts.push({ text: `${counts.cancelled} cancelled`, tone: "warning" });
  if (counts.unknown) parts.push({ text: `${counts.unknown} unknown`, tone: "neutral" });
  parts.push({ text: `${counts.succeeded} passed`, tone: "success" });
  if (counts.skipped) parts.push({ text: `${counts.skipped} skipped`, tone: "neutral" });
  return parts;
});

const environment = computed(() => [props.run.environment.runtime, props.run.environment.os].filter(Boolean).join(" on "));
const environmentFacts = computed(() => Object.entries(props.run.environment).sort(([left], [right]) => left.localeCompare(right)));
function ruleLabel(test: TestTrace) {
  const rule = diagnosisRule(test);
  return rule ? diagnosisRuleLabels[rule] : outcomeLabel(test.outcome);
}
function attentionReason(test: TestTrace) {
  const finding = diagnosisRule(test) === "finding" ? testFindings(test)[0] : undefined;
  return finding ? { title: finding.message, detail: [finding.status, finding.category, ...finding.tags].filter(Boolean).join(", ") } : failureReason(test);
}
function testSelection(test: TestTrace): { span: string } | undefined {
  return test.failure ? { span: test.failure.span.id } : undefined;
}
function gaps(test: TestTrace) {
  return untracedGaps(test).map(gap => ({
    left: timelinePercent(gap.start, props.run.start, props.run.duration),
    width: (gap.duration / Math.max(props.run.duration, 1)) * 100,
    title: `${formatDuration(gap.duration)} with no recorded operation in ${gap.phase}`
  }));
}
function operationBar(span: Span) {
  return { left: `${timelinePercent(span.start, props.run.start, props.run.duration)}%`, width: `${Math.max(0.4, span.duration / Math.max(props.run.duration, 1) * 100)}%` };
}

/*
 * One question per view instead of every panel under each other: what needs attention and what the run could
 * see; the run's clock; its own work; its identity; its files. A view with nothing in it has no tab.
 */
const tabs = computed(() => {
  const list: [RunView, string][] = [["overview", "Overview"], ["timeline", "Timeline"]];
  if (props.run.spans.length || props.run.items.length) list.push(["operations", "Operations"]);
  list.push(["details", "Details"]);
  if (files.value.length) list.push(["files", `Files ${files.value.length}`]);
  return list.map(([id, label]) => ({ id, label, href: href({ name: "run", view: id }) }));
});
const current = computed<RunView>(() => tabs.value.some(tab => tab.id === props.view) ? props.view : "overview");
const hasAttention = computed(() => attention.value.length > 0 || runProblems.value.spans.length > 0 || runProblems.value.moments.length > 0
  || props.run.findings.length > 0 || props.run.gates.length > 0);

/* The list follows the one test filter the rail shows, and keeps the run's own order: this list is a clock. */
const visible = computed(() => props.run.tests.filter(test =>
  matchesOutcome(test, testFilter.outcome) && testMatches(test, testFilter.query)));

/** Each test's phases placed on the run's own time axis, so parallel and slow tests show as such. */
function bars(test: TestTrace) {
  return phaseSegments(test).map(segment => ({
    phase: segment.phase,
    left: timelinePercent(segment.start, props.run.start, props.run.duration),
    width: Math.max(0.4, (segment.duration / Math.max(props.run.duration, 1)) * 100)
  }));
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
      <!-- The run's shape at a glance: one button per test, in the order they started, coloured by outcome. -->
      <div class="strip" role="group" :aria-label="`${run.tests.length} tests, in start order`">
        <button v-for="test in run.tests" :key="test.id" type="button" class="tick" :class="tone(test.outcome)"
                :aria-label="`${pad(test.number)} ${testTitle(test)}, ${test.outcome}`"
                :title="`${pad(test.number)} ${testTitle(test)}`" @click="emit('select', test)" />
      </div>
    </header>

    <div class="views">
      <Tabs :items="tabs" :active="current" variant="underline" label="Views of this run" panel="run-view" @select="emit('tab', $event)" />
    </div>

    <div id="run-view" role="tabpanel" :aria-labelledby="`run-view-tab-${current}`" class="view" :class="current">
    <template v-if="current === 'overview'">
    <Panel v-if="hasAttention" title="Needs attention" pad="none">
      <div class="attention">
        <button v-for="test in attention" :key="test.id" type="button" class="issue" :class="tone(test.outcome)" @click="emit('select', test, testSelection(test))">
          <b>{{ pad(test.number) }}</b>
          <span class="issue-main">
            <strong>{{ testTitle(test) }}</strong>
            <span class="issue-line"><span class="issue-kind">{{ ruleLabel(test) }}</span><span class="issue-reason">{{ attentionReason(test).title }}</span></span>
            <span v-if="attentionReason(test).detail" class="issue-detail">{{ attentionReason(test).detail }}</span>
          </span>
        </button>
        <button v-for="span in runProblems.spans" :key="`run-span-${span.id}`" type="button" class="issue run" :class="tone(span.status)" @click="emit('span', span)">
          <b>Run</b>
          <span class="issue-main">
            <strong>{{ span.name }}</strong>
            <span class="issue-line"><span class="issue-kind">{{ outcomeLabel(span.status) }}</span><span class="issue-reason">{{ problemReason(span.error, outcomeLabel(span.status)) }}</span></span>
          </span>
        </button>
        <div v-for="(moment, index) in runProblems.moments" :key="`run-moment-${index}`" class="issue run" :class="tone(moment.outcome)">
          <b>Run</b>
          <span class="issue-main">
            <strong>{{ moment.name }}</strong>
            <span class="issue-line"><span class="issue-kind">{{ outcomeLabel(moment.outcome) }}</span><span class="issue-reason">{{ problemReason(moment.error, outcomeLabel(moment.outcome)) }}</span></span>
          </span>
        </div>
        <component :is="item.test ? 'button' : 'div'" v-for="(item, index) in run.findings" :key="`finding-${index}`"
                   :type="item.test ? 'button' : undefined" class="issue finding" :class="item.finding.status.toLowerCase()"
                   @click="item.test && emit('select', item.test, findingSelection(item.test, item.finding.span))">
          <b>{{ item.test ? pad(item.test.number) : "Run" }}</b>
          <span class="issue-main">
            <strong>{{ item.finding.message }}</strong>
            <span class="issue-line"><span class="issue-kind">{{ item.finding.status }} finding</span><span class="issue-reason">{{ [item.finding.category, ...item.finding.tags].filter(Boolean).join(", ") }}</span></span>
          </span>
        </component>
        <div v-for="gate in run.gates" :key="gate.name" class="issue gate" :class="tone(gate.outcome)">
          <b>Gate</b>
          <span class="issue-main">
            <strong>{{ gate.name }}</strong>
            <span class="issue-line"><span class="issue-kind">{{ gate.status }}</span><span v-if="gate.message" class="issue-reason">{{ gate.message }}</span></span>
            <span v-if="gate.details" class="issue-detail">{{ gate.details }}</span>
          </span>
        </div>
      </div>
    </Panel>

    <section v-else class="all-clear">
      <strong>Nothing needs attention.</strong>
      <span>All {{ run.tests.length }} tests passed, with no findings and no failing run operations.</span>
    </section>

    <VisibilityStrip :visibility="run.visibility" :resources="resources" />
    </template>

    <Panel v-else-if="current === 'timeline'" title="Run timeline" subtitle="Tests in start order on the run's clock. Hatched time has no recorded operation." pad="none">
      <template #actions>
        <div class="filters">
          <OutcomeFilters :tests="run.tests" />
          <TextInput v-model="testFilter.query" type="search" placeholder="Find a test" label="Find a test" class="find" />
        </div>
      </template>
      <div class="legend" aria-hidden="true">
        <span v-for="phase in ['setup', 'execution', 'rollback', 'teardown']" :key="phase"><i :style="{ background: `var(--phase-${phase})` }" />{{ phase }}</span>
      </div>
      <div v-if="visible.length" class="scale" aria-hidden="true">
        <span class="axis"><i>start</i><i>{{ formatOffset(run.duration) }}</i></span>
      </div>
      <div v-if="visible.length" class="tests">
        <button v-for="test in visible" :key="test.id" type="button" class="test-row" :class="tone(test.outcome)"
                :title="testCodeName(test)" @click="emit('select', test)">
          <b>{{ pad(test.number) }}</b>
          <span class="test-name">
            <span class="name-line">
              <span>{{ testTitle(test) }}</span>
              <small>{{ testGroup(test) }}</small>
            </span>
            <small v-if="needsAttention(test)" class="reason">{{ failureReason(test).title }}</small>
          </span>
          <span class="bar">
            <i v-for="segment in bars(test)" :key="segment.phase"
               :style="{ left: `${segment.left}%`, width: `${segment.width}%`, background: `var(--phase-${segment.phase})` }" />
            <i v-for="(gap, index) in gaps(test)" :key="`gap-${index}`" class="gap" :title="gap.title" :style="{ left: `${gap.left}%`, width: `${gap.width}%` }" />
          </span>
          <small class="duration">{{ formatDuration(test.duration) }}</small>
          <i class="status" :class="tone(test.outcome)" aria-hidden="true" />
          <span class="visually-hidden">{{ outcomeLabel(test.outcome) }}</span>
        </button>
      </div>
      <EmptyState v-else message="No test matches this filter.">
        <FilterChip label="Show all tests" @select="resetTestFilter()" />
      </EmptyState>
    </Panel>

    <template v-else-if="current === 'operations'">
    <Panel v-if="run.spans.length" title="Run operations" subtitle="Work owned by the run, on the same clock as its tests." pad="none">
      <div class="scale" aria-hidden="true"><span class="axis"><i>start</i><i>{{ formatOffset(run.duration) }}</i></span></div>
      <div class="tests">
        <button v-for="span in run.spans" :key="span.id" type="button" class="test-row operation" :class="[tone(span.status), { selected: selectedSpan === span }]"
                :aria-pressed="selectedSpan === span" @click="emit('span', span)">
          <i class="status" :class="tone(span.status)" />
          <span class="test-name"><strong>{{ span.name }}</strong><small>{{ span.kind }}</small></span>
          <span class="bar"><i :style="operationBar(span)" /></span>
          <small class="duration">{{ formatDuration(span.duration) }}</small>
        </button>
      </div>
    </Panel>

    <Panel v-if="run.items.length" title="Run state" subtitle="Everything owned by this run. Select an item to see its changes and operations." pad="none">
      <div class="run-items">
        <button v-for="item in run.items" :key="item.key" type="button" :class="{ selected: selectedItem === item }" :aria-pressed="selectedItem === item" @click="emit('item', item)">
          <strong>{{ item.state['resource.description'] || item.name }}</strong><small>{{ item.kind }}</small>
        </button>
      </div>
    </Panel>

    </template>

    <Panel v-else-if="current === 'details'" title="Run details" subtitle="Identity and every environment value recorded in the trace.">
      <dl class="environment">
        <dt>Run id</dt><dd>{{ run.id }}</dd>
        <template v-for="[key, value] in environmentFacts" :key="key"><dt>environment.{{ key }}</dt><dd>{{ value }}</dd></template>
      </dl>
    </Panel>

    <Panel v-else-if="current === 'files'" title="Files" subtitle="Everything the run attached: reports, captured payloads, screenshots and browser traces." pad="none">
      <FileList :entries="files" @open="emit('artifact', $event)" />
    </Panel>
    </div>
  </div>
</template>

<style scoped>
.run { display: grid; gap: var(--space-4); align-content: start; container-type: inline-size; }
/* The view tabs stay in reach while a long view scrolls under them, as they do on a test. */
.views { position: sticky; top: 0; z-index: 3; margin-bottom: calc(var(--space-2) * -1); border-bottom: 1px solid var(--border); background: var(--bg); }
.view { min-width: 0; display: grid; gap: var(--space-4); align-content: start; }
/* The overview reads side by side when there is room: what needs attention, and what the run could see. */
@container (min-width: 960px) {
  .view.overview { grid-template-columns: minmax(0, 1.7fr) minmax(280px, 1fr); align-items: start; }
}
.all-clear { padding: var(--space-4); display: grid; gap: var(--space-1); border: 1px solid var(--border); border-left: 3px solid var(--success); border-radius: var(--radius-panel); background: var(--surface); }
.all-clear span { color: var(--muted); font-size: var(--text-meta); }


.summary {
  min-width: 0;
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
.strip { display: flex; flex-wrap: wrap; align-items: flex-end; align-content: flex-start; gap: 2px; min-height: 12px; }
/* Passing tests are the quiet majority; what did not pass stands up out of the line. */
.tick { flex: 1 1 0; min-width: 3px; height: 5px; padding: 0; border: 0; border-radius: var(--radius-hairline); background: var(--outcome-succeeded); opacity: .45; }
.tick:hover, .tick:focus-visible { opacity: 1; }
.tick.danger, .tick.warning { height: 12px; }
.tick.danger { background: var(--outcome-failed); opacity: 1; }
.tick.warning { background: var(--outcome-partial); opacity: 1; }
/* Skipped is planned, not run: a hollow tick keeps its place without reading as a pass. */
.tick.neutral { background: transparent; box-shadow: inset 0 0 0 1px var(--border-strong); opacity: 1; }

.attention { display: grid; }
.issue {
  width: 100%;
  padding: var(--space-3) var(--space-4);
  display: grid;
  grid-template-columns: 34px minmax(0, 1fr);
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
/* The rule and the reason share one line: what kind of failure, then where. */
.issue-line { min-width: 0; display: flex; flex-wrap: wrap; gap: 0 var(--space-2); font-size: var(--text-meta); }
.issue-reason { color: var(--muted); font-size: var(--text-meta); }
.issue-detail { overflow-wrap: anywhere; color: var(--text); font: var(--text-micro)/var(--leading) var(--font-mono); }
.issue-kind { color: var(--muted); font-size: var(--text-meta); font-weight: var(--weight-semibold); white-space: nowrap; }
.issue.danger .issue-kind { color: var(--danger); }
.issue.warning .issue-kind { color: var(--warning); }

.filters { display: flex; flex-wrap: wrap; align-items: center; justify-content: flex-end; gap: var(--space-2); }
.find { width: clamp(140px, 24cqi, 240px); }
.legend { padding: var(--space-2) var(--space-4) 0; display: flex; flex-wrap: wrap; gap: var(--space-4); color: var(--muted); font-size: var(--text-micro); text-transform: capitalize; }
.legend span { display: inline-flex; align-items: center; gap: var(--space-1); }
.legend i { width: 10px; height: 4px; border-radius: var(--radius-hairline); }
/* The bar's own clock: each row is placed on the run's axis, so the axis is labelled once above them. */
.scale { padding: var(--space-1) var(--space-2) 0; display: grid; grid-template-columns: 22px minmax(0, 1.1fr) minmax(0, 1fr) 58px 7px; gap: var(--space-3); }
.axis { grid-column: 3; display: flex; justify-content: space-between; color: var(--dim); font-size: var(--text-micro); }

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
  /* Rows are uniform and independent, so one row's change never re-lays-out the list. */
  contain: layout paint;
}
.test-row:hover { background: var(--hover); }
.test-row b { color: var(--dim); font: var(--text-micro) var(--font-mono); }
.test-row.danger b { color: var(--danger); }
.test-row.warning b { color: var(--warning); }
.test-name { min-width: 0; display: grid; }
.name-line { min-width: 0; display: flex; align-items: baseline; gap: var(--space-2); }
.name-line span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: var(--text-meta); }
.name-line small { flex: none; color: var(--dim); font-size: var(--text-micro); }
/* A test that did not pass says why in the list, so the reader can pick the right one without opening it. */
.reason { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: var(--text-micro); }
.test-row.danger .reason { color: var(--danger); }
.test-row.warning .reason { color: var(--warning); }
.test-row.neutral .reason { color: var(--muted); }
.bar { position: relative; height: 6px; border-radius: var(--radius-hairline); background: var(--surface-2); }
.bar i { position: absolute; top: 0; bottom: 0; border-radius: var(--radius-hairline); }
.bar i.gap { background: repeating-linear-gradient(135deg, var(--muted) 0, var(--muted) 1px, transparent 1px, transparent 4px); }
.operation .bar i { background: var(--blueprint); }
.operation strong { overflow-wrap: anywhere; font-size: var(--text-body); }
.selected { outline: 1px solid var(--blueprint); background: var(--blueprint-soft); }
.run-items { display: flex; flex-wrap: wrap; gap: var(--space-2); padding: var(--space-3) var(--space-4); }
.run-items button { min-width: 0; padding: var(--space-2) var(--space-3); display: flex; flex-wrap: wrap; gap: var(--space-2); border: 1px solid var(--border); border-radius: var(--radius-control); background: var(--surface-2); text-align: left; }
.run-items strong { overflow-wrap: anywhere; font-size: var(--text-body); }
.run-items small { color: var(--muted); font-size: var(--text-meta); }
.run-items button:hover { border-color: var(--blueprint); }
.environment { margin: 0; display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 2fr); gap: var(--space-1) var(--space-3); }
.environment dt, .environment dd { margin: 0; overflow-wrap: anywhere; font: var(--text-body)/var(--leading) var(--font-mono); }
.environment dt { color: var(--muted); }
.duration { color: var(--muted); font-size: var(--text-micro); text-align: right; font-variant-numeric: tabular-nums; }
.visually-hidden { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); }

@container (max-width: 640px) {
  .environment { grid-template-columns: minmax(0, 1fr); }
  .environment dd { padding-bottom: var(--space-2); }
  .test-row { grid-template-columns: 22px minmax(0, 1fr) 52px 7px; }
  .test-row .bar { grid-column: 2; grid-row: 2; }
  .test-row .duration { grid-column: 3; grid-row: 1; }
  .test-row > .status:last-of-type { grid-column: 4; grid-row: 1; }
  .scale { grid-template-columns: 22px minmax(0, 1fr) 52px 7px; }
  .scale .axis { grid-column: 2; }
  .issue { grid-template-columns: 30px minmax(0, 1fr); }
}
</style>
