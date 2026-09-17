<script setup lang="ts">
import { computed, ref, watch } from "vue";
import type { TestTrace, TraceEntry, TraceTreeItem } from "../model/trace-schema";
import { phaseOrder } from "../model/trace-schema";
import { buildTree, phaseSummary } from "../model/trace-rows";
import { lensMatches } from "../model/trace-levels";
import { searchMatches } from "../model/trace-search";
import { formatDuration, milliseconds } from "../model/trace-format";
import Panel from "../ui/Panel.vue";
import TextInput from "../ui/TextInput.vue";
import Tabs from "../ui/Tabs.vue";
import AppButton from "../ui/AppButton.vue";
import OutcomePill from "../ui/OutcomePill.vue";
import TraceTree from "../ui/TraceTree.vue";

const props = defineProps<{ test: TestTrace; selected?: TraceEntry }>();
const lens = ref("execution");
const query = ref("");
const emit = defineEmits<{ select: [entry: TraceEntry] }>();
const collapsed = ref(new Set<string>());

const phases = computed(() => phaseOrder
  .map(phase => {
    const entries = props.test.entries.filter(entry => entry.phase === phase);
    const tree = filterTree(buildTree(entries));
    // A phase and its `test.<phase>` operation are the same thing, so the header is that row: it carries
    // the duration, the outcome and the selection, and its children are the phase's top level.
    const lifecycle = tree.length === 1 && tree[0].entry.kind === `test.${phase.toLocaleLowerCase()}`
      ? tree[0]
      : undefined;
    return {
      phase,
      summary: phaseSummary(props.test, phase, entries),
      lifecycle: lifecycle?.entry,
      tree: lifecycle ? lifecycle.children : tree
    };
  })
  .filter(item => item.summary.entries.length && item.tree.length));

/** Every row's share bar is measured against the test, so depths stay comparable down the whole view. */
const span = computed(() => Math.max(milliseconds(props.test.duration), 1));

/** Keeps the ancestors of a match, so a lens or a search never hides the path that leads to it. */
function filterTree(items: TraceTreeItem[]): TraceTreeItem[] {
  return items
    .map(item => ({ entry: item.entry, children: filterTree(item.children) }))
    .filter(item => (lens.value === "execution" || lensMatches(item.entry, lens.value)) && (searchMatches(item.entry, query.value) || item.children.length));
}

const path = computed(() => {
  const ids = new Set<string>();
  if (!props.selected) return ids;
  const byId = new Map(props.test.entries.map(entry => [entry.id, entry]));
  let current: TraceEntry | undefined = props.selected;
  while (current?.parentId) { ids.add(current.parentId); current = byId.get(current.parentId); }
  return ids;
});
const shown = computed(() => phases.value.reduce((sum, group) => sum + count(group.tree), 0));
const hint = computed(() => {
  const filtering = lens.value !== "execution" || Boolean(query.value);
  const what = lens.value === "diagnostics" ? "failures, findings and gates" : lens.value === "lifecycle" ? "the orchestration" : query.value ? `“${query.value}”` : "the whole story";
  return filtering ? `Filtered to ${what}; ancestors stay visible` : "Every operation ProtoTest ran, exactly as it nests";
});

function count(items: TraceTreeItem[]): number {
  return items.reduce((sum, item) => sum + 1 + count(item.children), 0);
}
function toggle(id: string) {
  const next = new Set(collapsed.value);
  if (next.has(id)) next.delete(id); else next.add(id);
  collapsed.value = next;
}
function collapseAll() {
  collapsed.value = new Set(props.test.entries.map(entry => entry.id));
}
function expandAll() { collapsed.value = new Set(); }

// A selection made elsewhere — the failure banner, the story — has to be reachable here.
watch(() => props.selected?.id, () => {
  if (!path.value.size) return;
  const next = new Set(collapsed.value);
  for (const id of path.value) next.delete(id);
  collapsed.value = next;
}, { immediate: true });
</script>

<template>
  <Panel title="Execution tree" :subtitle="hint" pad="none">
    <template #actions>
      <TextInput v-model="query" type="search" placeholder="Search" label="Search this test" class="tree-search" />
      <Tabs :items="[{ id: 'execution', label: 'All' }, { id: 'lifecycle', label: 'Lifecycle' }, { id: 'diagnostics', label: 'Problems' }]" :active="lens" variant="pill" @select="lens = $event" />
      <AppButton variant="quiet" @click="collapsed.size ? expandAll() : collapseAll()">
        {{ collapsed.size ? "Expand all" : "Collapse all" }}
      </AppButton>
      <span class="count mono">{{ shown }} of {{ test.entries.length }}</span>
    </template>
    <div class="phases">
      <section v-for="group in phases" :key="group.phase" class="phase"
               :style="{ '--phase-color': `var(--phase-${group.phase.toLocaleLowerCase()})` }">
        <component :is="group.lifecycle ? 'button' : 'header'" class="phase-head"
                   :class="{ selectable: group.lifecycle, active: group.lifecycle && group.lifecycle.id === selected?.id }"
                   :type="group.lifecycle ? 'button' : undefined"
                   :title="group.lifecycle?.name"
                   @click="group.lifecycle && emit('select', group.lifecycle)">
          <span class="marker" />
          <strong>{{ group.phase }}</strong>
          <small>{{ group.summary.operations }} operations · {{ group.summary.events }} events</small>
          <span class="phase-time mono">{{ formatDuration(group.summary.duration) }}</span>
          <OutcomePill :outcome="group.summary.outcome" />
        </component>
        <TraceTree :items="group.tree" :depth="0" :collapsed="collapsed" :selected="selected" :path="path"
                   :span="span" @select="emit('select', $event)" @toggle="toggle" />
      </section>
    </div>
  </Panel>
</template>

<style scoped>
.count { color: var(--muted); font-size: var(--text-micro); }
.tree-search { width: clamp(120px, 14vw, 200px); }
.phases { padding: 0 var(--space-3) var(--space-4); container-type: inline-size; }
.phase + .phase { margin-top: var(--space-5); }
/* The phase header is the phase's own lifecycle operation: one row, not a heading above a copy of itself. */
.phase-head {
  position: sticky;
  top: 0;
  z-index: 1;
  width: 100%;
  padding: var(--space-2);
  display: grid;
  grid-template-columns: 8px auto minmax(0, 1fr) auto auto;
  align-items: center;
  gap: var(--space-3);
  border: 0;
  border-bottom: 1px solid var(--border);
  background: var(--surface);
  text-align: left;
}
.phase-head.selectable:hover { background: var(--hover); }
.phase-head.active { background: var(--blueprint-soft); box-shadow: inset 2px 0 0 var(--blueprint); }
.phase-head strong { color: var(--muted); font-size: var(--text-micro); letter-spacing: .08em; text-transform: uppercase; }
.phase-head small { color: var(--dim); font-size: var(--text-micro); }
.phase-time { color: var(--muted); font-size: var(--text-micro); }
.marker { width: 8px; height: 8px; border-radius: var(--radius-hairline); background: var(--phase-color, var(--dim)); }
/* The phase colours the trace inside it, so a row always says which phase it belongs to. */
.phase :deep(.guide.continues::before),
.phase :deep(.elbow::before),
.phase :deep(.elbow::after) { border-color: color-mix(in srgb, var(--phase-color) 30%, var(--border)); }
.phase :deep(.guide.continues.lit::before) { border-color: var(--blueprint-line); }
</style>
