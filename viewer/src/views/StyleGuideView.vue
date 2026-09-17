<script setup lang="ts">
import { ref } from "vue";
import type { TraceEntry, TraceOutcome, TracePhase, TraceTreeItem } from "../model/trace-schema";
import type { StoryStep as StoryStepModel } from "../model/trace-rows";
import Panel from "../ui/Panel.vue";
import AppButton from "../ui/AppButton.vue";
import TextInput from "../ui/TextInput.vue";
import MetricTile from "../ui/MetricTile.vue";
import OutcomePill from "../ui/OutcomePill.vue";
import KindChip from "../ui/KindChip.vue";
import FilterChip from "../ui/FilterChip.vue";
import PhaseKey from "../ui/PhaseKey.vue";
import Tabs from "../ui/Tabs.vue";
import TimelineRow from "../ui/TimelineRow.vue";
import TraceTree from "../ui/TraceTree.vue";
import EmptyState from "../ui/EmptyState.vue";
import ColumnResizer from "../ui/ColumnResizer.vue";
import StoryStep from "../ui/StoryStep.vue";
import FailureBanner from "../ui/FailureBanner.vue";

const sample = ref("");

const outcomes: TraceOutcome[] = ["Succeeded", "Partial", "Failed", "Skipped"];

/** The four entry-type families the token file defines, plus a kind the viewer has never seen. */
const kinds = [
  { id: "call", label: "Call" }, { id: "data", label: "Data" }, { id: "custom", label: "Northstar" },
  { id: "assertion", label: "Assertion" }, { id: "observation", label: "Observation" }, { id: "artifact", label: "Artifact" },
  { id: "finding", label: "Finding" }, { id: "gate", label: "Gate" },
  { id: "lifecycle", label: "Lifecycle" }, { id: "extension", label: "Extension" },
  { id: "client", label: "Client" }, { id: "context", label: "Context" },
  { id: "auth", label: "Authentication" }, { id: "ownership", label: "Ownership" }
];

const surfaces = ["--bg", "--surface", "--surface-2", "--hover", "--surface-sunken"];
const semantics = ["--blueprint", "--success", "--warning", "--danger", "--violet", "--muted"];
const radii = ["--radius-chip", "--radius-control", "--radius-panel", "--radius-overlay"];
const spacing = ["--space-1", "--space-2", "--space-3", "--space-4", "--space-5", "--space-6", "--space-7"];
const typeScale = [
  { token: "--text-display", label: "Display" }, { token: "--text-heading", label: "Heading" },
  { token: "--text-title", label: "Title" }, { token: "--text-strong", label: "Strong" },
  { token: "--text-body", label: "Body" }, { token: "--text-meta", label: "Meta" },
  { token: "--text-micro", label: "Micro" }
];

const tabItems = [
  { id: "story", label: "Story" },
  { id: "lifecycle", label: "Lifecycle" },
  { id: "tree", label: "Tree" }
];
const filterItems = [
  { id: "all", label: "All", count: 37, tone: "neutral" },
  { id: "failed", label: "Failed", count: 1, tone: "danger" },
  { id: "partial", label: "Partial", count: 2, tone: "warning" },
  { id: "passed", label: "Passed", count: 34, tone: "success" }
] as const;
const tab = ref("story");
const filter = ref<string>("all");

const timelineRows = [
  { name: "AProjectProvisionedThroughTheDomainIsVisibleToTheApplication", group: "DomainAccessJourney", outcome: "Succeeded" as TraceOutcome, duration: 2390, offset: 0, width: 88, segments: [{ phase: "Setup" as TracePhase, outcome: "Succeeded" as TraceOutcome, left: 0, width: 57 }, { phase: "Execution" as TracePhase, outcome: "Succeeded" as TraceOutcome, left: 57, width: 40 }, { phase: "Teardown" as TracePhase, outcome: "Succeeded" as TraceOutcome, left: 97, width: 3 }] },
  { name: "TheIntentionalFailureShowcaseStaysOptIn", group: "DiagnosticsShowcase", outcome: "Failed" as TraceOutcome, duration: 37, offset: 72, width: 6, segments: [{ phase: "Execution" as TracePhase, outcome: "Failed" as TraceOutcome, left: 0, width: 100 }] },
  { name: "AFailedOperationRecordsItsDiagnosticsAndTheRunContinues", group: "DiagnosticsShowcase", outcome: "Partial" as TraceOutcome, duration: 1630, offset: 20, width: 61, segments: [{ phase: "Setup" as TracePhase, outcome: "Succeeded" as TraceOutcome, left: 0, width: 62 }, { phase: "Execution" as TracePhase, outcome: "Partial" as TraceOutcome, left: 62, width: 30 }] }
];

let sequence = 0;
function entry(kind: string, name: string, duration: number, outcome: TraceOutcome, parentId?: string): TraceEntry {
  sequence += 1;
  return {
    id: `fixture-${sequence}`,
    parentId,
    entryKind: "Operation",
    kind,
    name,
    source: "ProtoTest.Core",
    phase: "Setup" as TracePhase,
    timestampUtc: new Date(2026, 8, 17, 20, 14, 2).toISOString(),
    duration,
    outcome,
    attributes: {}
  };
}
const parent = entry("hook.before", "Before · NorthstarScenarioHook", 6, "Succeeded");
const child = entry("client.resolve", "Resolve · ScenarioProbe (ScenarioProbe)", 0.4, "Succeeded", parent.id);
const grandchild = entry("client.try_resolve", "Try resolve · Northstar (HttpClient)", 0.1, "Succeeded", child.id);
const second = entry("attribute.before", "Before · NorthstarTenantAttribute", 232, "Succeeded");
const call = entry("http.request", "REST · POST /test-support/tenants", 227, "Succeeded", second.id);
const tree: TraceTreeItem[] = [
  { entry: parent, children: [{ entry: child, children: [{ entry: grandchild, children: [] }] }] },
  { entry: second, children: [{ entry: call, children: [] }] }
];
const failed = entry("northstar.webhook.deliver", "Deliver subscription webhook", 0.145, "Failed");
failed.error = { type: "System.TimeoutException", message: "The billing ledger did not acknowledge the webhook within 2 seconds." };
failed.attributes = { "webhook.destination": "billing-ledger", "webhook.attempt": "3" };
const assertion = entry("assert.http.status", "Assert status · 201 Created", 0.002, "Succeeded", call.id);
assertion.attributes = { "actual.status_code": "201" };
const observation = entry("observation.record", "Observation · http.response", 0, "Succeeded", call.id);
observation.attributes = { "observation.kind": "http.response" };
call.attributes = { "http.request.method": "POST", "http.route": "/test-support/tenants", "http.response.status_code": "201" };
const storyStep: StoryStepModel = {
  entry: second,
  items: [{
    type: "step",
    step: {
      entry: call,
      items: [{ type: "evidence", entry: observation }, { type: "evidence", entry: assertion }],
      machinery: [grandchild, child]
    }
  }],
  machinery: [child, grandchild, parent]
};
const storyOpened = ref(new Set<string>());
const storyCollapsed = ref(new Set<string>());
function toggleStory(id: string) {
  const next = new Set(storyOpened.value);
  if (next.has(id)) next.delete(id); else next.add(id);
  storyOpened.value = next;
}

const collapsed = ref(new Set<string>());
function toggle(id: string) {
  const next = new Set(collapsed.value);
  if (next.has(id)) next.delete(id); else next.add(id);
  collapsed.value = next;
}
</script>

<template>
  <div class="styleguide">
    <header class="intro">
      <span class="eyebrow">ProtoTest design system</span>
      <h1>ProtoTrace style guide</h1>
      <p>Every primitive rendered from the components the viewer ships, and every token read from
        <code>design/prototest-tokens.css</code> — the one file the viewer, the docs and the HTML report share.
        If something appears in the product but not here, the primitive is missing.</p>
    </header>

    <Panel title="Surfaces and semantics" subtitle="Two surfaces, one palette. Colour only carries meaning.">
      <div class="swatches">
        <div v-for="token in surfaces" :key="token" class="swatch">
          <i :style="{ background: `var(${token})` }" /><code>{{ token }}</code>
        </div>
      </div>
      <div class="swatches">
        <div v-for="token in semantics" :key="token" class="swatch">
          <i :style="{ background: `var(${token})` }" /><code>{{ token }}</code>
        </div>
      </div>
    </Panel>

    <Panel title="Phases" subtitle="The four phases a test runs through, in order, coloured once for every view.">
      <PhaseKey />
    </Panel>

    <Panel title="Type scale" subtitle="Seven steps, nothing below 10px. Mono is for data, never for prose.">
      <ul class="scale">
        <li v-for="step in typeScale" :key="step.token">
          <span :style="{ fontSize: `var(${step.token})` }">{{ step.label }}</span>
          <code>{{ step.token }}</code>
        </li>
      </ul>
    </Panel>

    <Panel title="Spacing and radii" subtitle="Six spacing steps; four radii, one per role.">
      <div class="spacing">
        <div v-for="token in spacing" :key="token" class="space">
          <i :style="{ width: `var(${token})`, height: `var(${token})` }" /><code>{{ token }}</code>
        </div>
      </div>
      <div class="swatches radii">
        <div v-for="token in radii" :key="token" class="swatch">
          <i class="radius" :style="{ borderRadius: `var(${token})` }" /><code>{{ token }}</code>
        </div>
      </div>
    </Panel>

    <Panel title="Panel" subtitle="The container everything lives in. Header 44px, radius 10, border only — shadow belongs to overlays.">
      <template #actions><AppButton variant="quiet">Action</AppButton></template>
      <p class="note">Body padding is <code>--space-4</code> by default; <code>pad="tight"</code> for lists, <code>pad="none"</code> for tables.</p>
    </Panel>

    <Panel title="Buttons" subtitle="30px tall, radius 7. Primary acts, secondary supports, quiet recedes, icon is square.">
      <div class="row-of">
        <AppButton variant="primary">Open trace</AppButton>
        <AppButton>Choose file</AppButton>
        <AppButton variant="quiet">Filter</AppButton>
        <AppButton variant="icon" label="Toggle theme">☀</AppButton>
        <AppButton :disabled="true">Disabled</AppButton>
      </div>
    </Panel>

    <Panel title="Input" subtitle="30px tall. Focus is a blueprint border plus a 3px soft ring.">
      <div class="narrow"><TextInput v-model="sample" type="search" placeholder="Search this test" /></div>
    </Panel>

    <Panel title="Metric tiles" subtitle="Label 11, value 15 mono. Colour only when the number means something.">
      <div class="row-of">
        <MetricTile label="Tests" :value="37" />
        <MetricTile label="Passed" :value="34" tone="success" />
        <MetricTile label="Partial" :value="2" tone="warning" />
        <MetricTile label="Failed" :value="1" tone="danger" />
        <MetricTile label="Duration" value="2.63 s" />
      </div>
    </Panel>

    <Panel title="Outcome pill" subtitle="A dot and a word. Never colour alone.">
      <div class="row-of"><OutcomePill v-for="outcome in outcomes" :key="outcome" :outcome="outcome" :detail="outcome === 'Succeeded' ? '2.39 s' : undefined" /></div>
    </Panel>

    <Panel title="Kind chips" subtitle="Four families: what a test did, what it proved, what it decided, and the machinery that carried it. An unknown kind — an integration's own — reads as an action.">
      <div class="row-of"><KindChip v-for="kind in kinds" :key="kind.id" :type="kind" /></div>
    </Panel>

    <Panel title="Tabs and filters" subtitle="Underline navigates between views; chips filter a list. Never both styles in one control.">
      <div class="stack">
        <Tabs :items="tabItems" :active="tab" @select="tab = $event" />
        <div class="row-of">
          <FilterChip v-for="item in filterItems" :key="item.id" :label="item.label" :count="item.count"
                      :tone="item.tone" :active="filter === item.id" @select="filter = item.id" />
        </div>
      </div>
    </Panel>

    <Panel title="Timeline row" subtitle="Number, name, phase-split bar on a shared axis, duration. The run's test list." pad="tight">
      <TimelineRow v-for="(row, index) in timelineRows" :key="row.name" :index="index + 1" v-bind="row" />
    </Panel>

    <Panel title="Failure banner" subtitle="Above the test, before anything else: the operation that explains the outcome, and a way into it.">
      <FailureBanner :entry="failed" outcome="Partial" />
    </Panel>

    <Panel title="Story step" subtitle="An operation, the evidence it produced, the operations it carried, and the plumbing it took — folded until asked for." pad="tight">
      <StoryStep :step="storyStep" :depth="0" :collapsed="storyCollapsed" :opened="storyOpened"
                 @collapse="(id: string) => storyCollapsed.has(id) ? storyCollapsed.delete(id) : storyCollapsed.add(id)" @toggle="toggleStory" />
    </Panel>

    <Panel title="Tree" subtitle="An execution trace, not a file list: drawn branches, a checkpoint coloured by entry type, the share of the test each operation took, and full keyboard navigation." pad="tight">
      <TraceTree :items="tree" :depth="0" :collapsed="collapsed" :span="300" @toggle="toggle" />
    </Panel>

    <Panel title="Column resizer" subtitle="Between two columns: a hairline at rest, a blueprint line under the pointer. Double-click restores the default width.">
      <div class="resize-demo">
        <div>Rail</div>
        <ColumnResizer label="Resize the demo column" />
        <div>View</div>
      </div>
    </Panel>

    <Panel title="Empty state" subtitle="One sentence and one action.">
      <EmptyState message="No test matches this filter."><AppButton variant="quiet">Clear filter</AppButton></EmptyState>
    </Panel>
  </div>
</template>

<style scoped>
.styleguide {
  width: min(1100px, 100%);
  margin: 0 auto;
  padding: var(--space-4) var(--space-4) var(--space-7);
  display: grid;
  gap: var(--space-4);
  align-content: start;
}
.intro h1 { margin: var(--space-1) 0; font-size: var(--text-heading); }
.intro p { max-width: 70ch; color: var(--muted); font-size: var(--text-body); }
.row-of { display: flex; flex-wrap: wrap; align-items: center; gap: var(--space-3); }
.stack { display: grid; gap: var(--space-4); }
.narrow { max-width: 260px; }
.note { color: var(--muted); font-size: var(--text-meta); }
code { padding: 1px var(--space-2); border-radius: var(--radius-chip); background: var(--surface-2); color: var(--muted); font-family: var(--font-mono); font-size: var(--text-micro); }

.swatches { display: flex; flex-wrap: wrap; gap: var(--space-3); }
.swatches + .swatches { margin-top: var(--space-3); }
.swatch { display: flex; align-items: center; gap: var(--space-2); }
.swatch i { width: 28px; height: 20px; border: 1px solid var(--border-strong); border-radius: var(--radius-chip); }
.radii .radius { width: 40px; height: 28px; border: 1px solid var(--blueprint); background: var(--blueprint-soft); }

.spacing { display: flex; flex-wrap: wrap; align-items: flex-end; gap: var(--space-4); }
.space { display: flex; flex-direction: column; align-items: center; gap: var(--space-2); }
.space i { display: block; background: var(--blueprint); border-radius: var(--radius-hairline); }

.scale { margin: 0; padding: 0; display: grid; gap: var(--space-2); list-style: none; }
.scale li { display: flex; align-items: baseline; gap: var(--space-4); }
.scale span { min-width: 140px; font-family: var(--font-display); }

.resize-demo { height: 80px; display: grid; grid-template-columns: 1fr 7px 2fr; }
.resize-demo > div { display: grid; place-items: center; border: 1px solid var(--border); border-radius: var(--radius-control); background: var(--surface-2); color: var(--muted); font-size: var(--text-meta); }
</style>
