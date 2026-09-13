<script setup lang="ts">
import { computed, ref } from "vue";
import type { TestTrace, TraceEntry } from "../trace-schema";
import { buildTree, matchesView, phaseOrder, phaseSummary, testDisplayName, testGroupName, tone } from "../trace-utils";
import TraceTreeNode from "./TraceTreeNode.vue";

const props = defineProps<{ test: TestTrace; selected?: TraceEntry | null }>();
const emit = defineEmits<{ select: [entry: TraceEntry] }>();
const query = ref("");
const collapsed = ref(new Set<string>());
const activeView = ref("flow");
const diagnosticErrors = computed(() => {
  const failed = props.test.entries.filter(entry => entry.outcome === "Failed" || entry.error);
  const specific = failed.filter(entry => !entry.kind.startsWith("test."));
  return specific.length ? specific : failed;
});
function matchesActiveView(entry: TraceEntry, view = activeView.value) {
  return view === "errors" ? diagnosticErrors.value.some(error => error.id === entry.id) : matchesView(entry, view);
}
const viewDefinitions = computed(() => [
  { id: "flow", label: "Flow", description: "The operation hierarchy and concrete failures, without routine diagnostic events.", count: props.test.entries.filter(entry => matchesView(entry, "flow")).length },
  { id: "trace", label: "All events", description: "Every recorded operation and event. Use this for complete forensic detail.", count: props.test.entries.length },
  { id: "network", label: "Network", description: "HTTP and GraphQL requests, routing, responses and protocol-level diagnostics.", count: props.test.entries.filter(entry => matchesView(entry, "network")).length },
  { id: "assertions", label: "Assertions", description: "Verification steps such as status-code and response-shape checks.", count: props.test.entries.filter(entry => matchesView(entry, "assertions")).length },
  { id: "observations", label: "Observations", description: "Intentional domain facts recorded for reporting; separate from automatic execution tracing.", count: props.test.entries.filter(entry => matchesView(entry, "observations")).length },
  { id: "artifacts", label: "Artifacts", description: "Files attached to this test, including their registration and publication lifecycle.", count: props.test.entries.filter(entry => matchesView(entry, "artifacts")).length },
  { id: "errors", label: "Errors", description: "Concrete diagnostic failures. Wrapper operations remain visible only to preserve context.", count: diagnosticErrors.value.length }
].filter(item => item.id === "flow" || item.id === "trace" || item.count));
const activeDescription = computed(() => viewDefinitions.value.find(item => item.id === activeView.value));

const constrained = computed(() => Boolean(query.value.trim()) || activeView.value !== "trace");
const visibleEntries = computed(() => {
  if (!constrained.value) return props.test.entries;
  const byId = new Map(props.test.entries.map(entry => [entry.id, entry]));
  const visible = new Set<string>();
  const search = query.value.trim().toLocaleLowerCase();
  props.test.entries
    .filter(entry => matchesActiveView(entry))
    .filter(entry => !search || JSON.stringify([entry.name, entry.kind, entry.source, entry.attributes]).toLocaleLowerCase().includes(search))
    .forEach(entry => {
      let current: TraceEntry | undefined = entry;
      while (current) { visible.add(current.id); current = current.parentId ? byId.get(current.parentId) : undefined; }
    });
  return props.test.entries.filter(entry => visible.has(entry.id));
});
const phases = computed(() => phaseOrder
  .map(phase => ({ ...phaseSummary(props.test, phase), tree: buildTree(visibleEntries.value.filter(entry => entry.phase === phase)) }))
  .filter(phase => phase.tree.length));

function toggle(id: string) {
  const next = new Set(collapsed.value);
  next.has(id) ? next.delete(id) : next.add(id);
  collapsed.value = next;
}
function collapseAll() { collapsed.value = new Set(props.test.entries.filter(entry => entry.entryKind === "Operation").map(entry => entry.id)); }
</script>

<template>
  <section class="trace-panel">
    <div class="panel-heading trace-toolbar">
      <div><strong :title="test.name">{{ testDisplayName(test) }}</strong><span>{{ testGroupName(test) }} · {{ test.entries.length }} entries · {{ test.outcome }}</span></div>
      <div class="tree-actions" role="group" aria-label="Tree controls">
        <button type="button" @click="collapsed = new Set()">Expand all</button>
        <button type="button" @click="collapseAll">Collapse all</button>
      </div>
    </div>
    <nav class="view-tabs" aria-label="Trace filters">
      <button v-for="item in viewDefinitions" :key="item.id" type="button" :class="{ active: activeView === item.id }" :title="item.description" @click="activeView = item.id">
        {{ item.label }} <span>{{ item.count }}</span>
      </button>
    </nav>
    <p v-if="activeDescription" class="filter-description"><strong>{{ activeDescription.label }}:</strong> {{ activeDescription.description }}</p>
    <label class="search trace-search"><span aria-hidden="true">⌕</span><input v-model="query" type="search" placeholder="Find operation, source, kind, or attribute" aria-label="Filter trace entries"></label>
    <div class="trace-list">
      <section v-for="phase in phases" :key="phase.phase" class="trace-phase" :class="`phase-${phase.phase.toLocaleLowerCase()}`">
        <header class="trace-phase-heading">
          <div><span class="phase-marker" /><strong>{{ phase.phase }}</strong><small>{{ phase.operations }} operations · {{ phase.events }} events</small></div>
          <span><i class="status" :class="tone(phase.outcome)" />{{ phase.outcome }}</span>
        </header>
        <div class="phase-entries">
          <TraceTreeNode v-for="item in phase.tree" :key="item.entry.id" :item="item" :depth="0" :test="test"
                         :selected-id="selected?.id" :collapsed="collapsed" :force-expanded="Boolean(query.trim())"
                         @select="emit('select', $event)" @toggle="toggle" />
        </div>
      </section>
      <p v-if="!phases.length" class="muted empty-list">No entries match this view.</p>
    </div>
  </section>
</template>
