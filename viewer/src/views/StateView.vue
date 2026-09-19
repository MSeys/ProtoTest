<script setup lang="ts">
import { computed } from "vue";
import type { Change, Item, Span, TestTrace } from "../trace/model";
import { formatOffset, itemKindLabel, itemTitle, sourceLabels } from "../trace/format";
import EmptyState from "../ui/EmptyState.vue";
import Panel from "../ui/Panel.vue";

/*
 * What existed while the test ran and how it changed: one row per tracked item, a lifeline across the
 * test's duration, and a tick for every change. A tick is the link back to the timeline - it names and
 * selects the operation that caused it - so state and story explain each other.
 */
const props = defineProps<{ test: TestTrace; selected?: { kind: string; id: string }; selectedSpan?: string }>();
const emit = defineEmits<{ selectItem: [item: Item]; selectSpan: [span: Span] }>();

const infrastructure = new Set(["client", "server", "database", "context", "auth"]);

/**
 * Groups in the order a reader asks about them: what the application itself reported, then the domain
 * values the test tracked, then the machinery the test ran on.
 */
const groups = computed(() => {
  const items = props.test.items;
  const reported = items.filter(item => item.changes.some(change => change.source === "applicationside"));
  const domain = items.filter(item => !reported.includes(item) && !infrastructure.has(item.kind));
  const machinery = items.filter(item => !reported.includes(item) && infrastructure.has(item.kind));
  return [
    { id: "reported", title: "Reported by the application", note: "Values the application's own instrumentation reported while the test ran.", items: reported },
    { id: "domain", title: "Tracked by the test", note: "Values the test recorded about the system under test.", items: domain },
    { id: "machinery", title: "What the test ran on", note: "Clients, servers, contexts and connections, with who owned them and when they were released.", items: machinery }
  ].filter(group => group.items.length);
});

function position(at: number): string {
  const total = Math.max(props.test.duration, 1);
  return `${Math.min(100, Math.max(0, ((at - props.test.start) / total) * 100))}%`;
}

function lifeline(item: Item) {
  const start = Math.max(item.firstSeen, props.test.start);
  const end = Math.max(start, Math.min(item.lastSeen || props.test.end, props.test.end));
  const total = Math.max(props.test.duration, 1);
  return {
    left: position(start),
    width: `${Math.max(0.6, ((end - start) / total) * 100)}%`
  };
}

/** The state a reader wants on the row: the item's own name fields first, a count of the rest. */
function summary(item: Item): string {
  const entries = Object.entries(item.state).filter(([, value]) => value !== null && value !== "");
  const shown = entries.slice(0, 2).map(([key, value]) => `${key.split(".").at(-1)} ${value}`);
  return entries.length > 2 ? `${shown.join(", ")}, and ${entries.length - 2} more` : shown.join(", ");
}

function tickTitle(change: Change): string {
  return `${change.change} at ${formatOffset(change.at - props.test.start)} (${sourceLabels[change.source]})${change.span ? ` by ${change.span.name}` : ""}`;
}

function isSelected(item: Item) {
  return props.selected?.kind === item.kind && props.selected.id === item.id;
}
</script>

<template>
  <div class="state">
    <Panel v-for="group in groups" :key="group.id" :title="group.title" :subtitle="group.note" pad="none">
      <div class="scale" aria-hidden="true"><span>start</span><span>{{ formatOffset(test.duration) }}</span></div>
      <div v-for="item in group.items" :key="item.key" class="item" :class="{ active: isSelected(item) }">
        <button type="button" class="name" :title="item.id" @click="emit('selectItem', item)">
          <span class="kind">{{ itemKindLabel(item).label }}</span>
          <strong>{{ itemTitle(item) }}</strong>
          <small>{{ summary(item) }}</small>
        </button>
        <span class="track">
          <i class="life" :style="lifeline(item)" />
          <button v-for="(change, index) in item.changes" :key="index" type="button" class="tick" :class="[change.source, { active: change.span?.id === selectedSpan }]"
                  :style="{ left: position(change.at) }" :title="tickTitle(change)" :aria-label="tickTitle(change)"
                  :disabled="!change.span" @click="change.span && emit('selectSpan', change.span)" />
        </span>
      </div>
    </Panel>
    <EmptyState v-if="!groups.length" message="This test tracked no state: no clients, contexts or values were recorded." />
    <p class="legend">
      <span><i class="tick testside" />Test side</span>
      <span><i class="tick observed" />Observed</span>
      <span><i class="tick applicationside" />Application</span>
      <span class="hint">A tick is a change; select it to see the operation that made it.</span>
    </p>
  </div>
</template>

<style scoped>
.state { display: grid; gap: var(--space-4); container-type: inline-size; }
.scale { margin-left: calc(38% + var(--space-3)); padding: var(--space-1) var(--space-4) 0 0; display: flex; justify-content: space-between; color: var(--dim); font-size: var(--text-micro); }

.item {
  padding: 0 var(--space-4) 0 var(--space-2);
  transition: background var(--motion-fast) var(--motion-ease);
  display: grid;
  grid-template-columns: 38% minmax(0, 1fr);
  align-items: center;
  gap: var(--space-3);
}
.item:hover { background: var(--hover); }
.item.active { background: var(--blueprint-soft); box-shadow: inset 2px 0 0 var(--blueprint); }
.name {
  min-width: 0;
  min-height: var(--control-height);
  padding: var(--space-1) var(--space-2);
  display: grid;
  grid-template-columns: auto minmax(0, 1fr);
  align-items: baseline;
  gap: 0 var(--space-2);
  border: 0;
  background: transparent;
  text-align: left;
}
.kind { color: var(--muted); font-size: var(--text-micro); font-weight: var(--weight-semibold); }
.name strong { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: var(--text-meta); font-weight: var(--weight-semibold); }
.name small { grid-column: 2; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--dim); font-size: var(--text-micro); }

.track { position: relative; height: 18px; }
.track::before { content: ""; position: absolute; left: 0; right: 0; top: 50%; border-top: 1px dashed var(--border); }
/* Blueprint to solid: the dashed axis is the test's time; the solid line is when the item existed. */
.life { position: absolute; top: calc(50% - 1px); height: 2px; border-radius: var(--radius-hairline); background: var(--border-strong); }
.tick {
  position: absolute;
  top: 3px;
  width: 6px;
  height: 12px;
  transform: translateX(-50%);
  padding: 0;
  border: 0;
  border-radius: var(--radius-hairline);
  background: var(--muted);
}
.tick:disabled { cursor: default; }
.tick:not(:disabled):hover, .tick.active { outline: 2px solid var(--blueprint); outline-offset: 1px; }
.tick.testside { background: var(--muted); }
.tick.observed { background: var(--pt-cyan); }
.tick.applicationside { background: var(--blueprint); }

.legend { display: flex; flex-wrap: wrap; gap: var(--space-2) var(--space-4); color: var(--muted); font-size: var(--text-micro); }
.legend span { display: inline-flex; align-items: center; gap: var(--space-1); }
.legend .tick { position: static; width: 5px; height: 10px; margin: 0; }
.legend .hint { color: var(--dim); }

@container (max-width: 560px) {
  .item { grid-template-columns: minmax(0, 1fr); gap: 0; padding-bottom: var(--space-2); }
  .scale { display: none; }
  .track { margin: 0 var(--space-2); }
}
</style>
