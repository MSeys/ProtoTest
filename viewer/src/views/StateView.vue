<script setup lang="ts">
import { computed, nextTick, ref, watch } from "vue";
import type { Change, Item, Span, TestTrace } from "../trace/model";
import { formatDuration, formatOffset, itemKindLabel, itemTitle, phaseSegments, plural, rulerTicks, sourceLabels, timelinePercent } from "../trace/format";
import EmptyState from "../ui/EmptyState.vue";
import Panel from "../ui/Panel.vue";
import { frameworkMode } from "../ui/frameworkMode";

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
  ].filter(group => group.items.length)
    // The machinery follows the Framework switch like the operations do; a selected item keeps it in view.
    .filter(group => group.id !== "machinery" || frameworkMode.value !== "hide" || group.items.some(isSelected));
});

function position(at: number): string {
  return `${timelinePercent(at, props.test.start, props.test.duration)}%`;
}

function lifeline(item: Item) {
  const start = Math.max(item.firstSeen, props.test.start);
  const end = Math.max(start, Math.min(item.lastSeen, props.test.end));
  return {
    left: position(start),
    width: `${Math.max(0.6, ((end - start) / Math.max(props.test.duration, 1)) * 100)}%`
  };
}

const ticks = computed(() => rulerTicks(0, props.test.duration).map(at => ({ label: at === 0 ? "0" : formatDuration(at), left: position(props.test.start + at) })));
const phases = computed(() => phaseSegments(props.test).map(phase => ({ ...phase, left: position(phase.start), width: `${phase.duration / Math.max(props.test.duration, 1) * 100}%` })));
const selectedOperation = computed(() => props.selectedSpan ? props.test.byId.get(props.selectedSpan) ?? props.test.spans.find(span => span.id === props.selectedSpan) : undefined);
const selectedTime = computed(() => {
  const span = selectedOperation.value;
  if (!span) return undefined;
  const left = timelinePercent(span.start, props.test.start, props.test.duration);
  const right = timelinePercent(span.end, props.test.start, props.test.duration);
  return { left: `${left}%`, width: `max(2px, ${Math.max(0, right - left)}%)` };
});

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

/** The selected change per item, when the selection names its operation: each cause, in words. */
const causes = computed(() => {
  const found = new Map<Item, Change>();
  if (!props.selectedSpan) return found;
  for (const item of props.test.items) {
    const change = item.changes.find(entry => entry.span?.id === props.selectedSpan);
    if (change) found.set(item, change);
  }
  return found;
});

// A selection made elsewhere - the failure card, the story, a shared link - has to be visible here.
const root = ref<HTMLElement>();
watch(() => props.selectedSpan, () => {
  void nextTick(() => root.value?.querySelector(".tick.active")?.scrollIntoView({ block: "nearest" }));
}, { immediate: true });
</script>

<template>
  <div ref="root" class="state">
    <p v-if="selectedOperation" class="selection" title="Its time is shaded; the items it touched are highlighted."><strong>{{ selectedOperation.name }}</strong>, {{ formatOffset(selectedOperation.start - test.start) }} to {{ formatOffset(selectedOperation.end - test.start) }}</p>
    <Panel v-for="group in groups" :key="group.id" :title="group.title" :subtitle="group.note" pad="none"
           :class="{ dim: group.id === 'machinery' && frameworkMode === 'dim' }">
      <div class="scale" :aria-label="`Test clock, 0 to ${formatDuration(test.duration)}`">
        <div class="ruler"><span v-for="tick in ticks" :key="tick.left" :style="{ left: tick.left }">{{ tick.label }}</span></div>
        <div class="phases"><i v-for="phase in phases" :key="phase.phase" :title="`${phase.phase}, ${formatDuration(phase.duration)}`" :style="{ left: phase.left, width: phase.width, background: `var(--phase-${phase.phase})` }" /></div>
      </div>
      <div v-for="item in group.items" :key="item.key" class="item" :class="{ active: isSelected(item), related: causes.has(item) || selectedOperation?.item === item }">
        <!-- Two lines: what it is, and what it holds. The ticks on the lifeline are its changes; their count is a hover away. -->
        <button type="button" class="name" :title="`${item.id}, ${item.changes.length} ${plural(item.changes.length, 'change')}`" :aria-pressed="isSelected(item)" @click="emit('selectItem', item)">
          <span class="kind">{{ itemKindLabel(item).label }}</span>
          <strong>{{ itemTitle(item) }}</strong>
          <small>{{ summary(item) }}</small>
        </button>
        <span class="track">
          <i v-if="selectedTime" class="selected-time" :style="selectedTime" />
          <i class="life" :style="lifeline(item)" />
          <button v-for="(change, index) in item.changes" :key="index" type="button" class="tick" :class="[change.source, { active: change.span?.id === selectedSpan, inferred: change.inferred }]"
                  :style="{ left: position(change.at) }" :title="tickTitle(change)" :aria-label="tickTitle(change)"
                  :disabled="!change.span" @click="change.span && emit('selectSpan', change.span)" :aria-current="change.span && change.span.id === selectedSpan ? 'true' : undefined" />
        </span>
        <p v-if="causes.get(item)" class="cause">{{ causes.get(item)!.change }} by {{ causes.get(item)!.span!.name }}</p>
      </div>
    </Panel>
    <EmptyState v-if="!groups.length" message="This test tracked no state: no clients, contexts or values were recorded." />
    <p class="legend">
      <span><i class="tick testside" />Test side</span>
      <span><i class="tick observed" />Observed</span>
      <span><i class="tick applicationside" />Application</span>
    </p>
  </div>
</template>

<style scoped>
.state { display: grid; gap: var(--space-4); container-type: inline-size; }
.scale { margin-block: var(--space-2) var(--space-1); padding: 0 var(--space-4) 0 var(--space-2); display: grid; grid-template-columns: 38% minmax(0, 1fr); column-gap: var(--space-3); color: var(--dim); font-size: var(--text-meta); }
.ruler { grid-column: 2; position: relative; height: 24px; border-bottom: 1px solid var(--border-strong); }
.ruler span { position: absolute; bottom: var(--space-1); transform: translateX(-50%); font-variant-numeric: tabular-nums; white-space: nowrap; }
.ruler span:first-child { transform: none; }
.ruler span::after { content: ""; position: absolute; height: 4px; bottom: calc(var(--space-1) * -1); left: 50%; border-left: 1px solid var(--border-strong); }
.phases { grid-column: 2; position: relative; height: 5px; margin-top: var(--space-1); }
.phases i { position: absolute; height: 3px; }
.selection { color: var(--muted); font-size: var(--text-body); overflow-wrap: anywhere; }
.selection strong { color: var(--text); }

.item {
  padding: 0 var(--space-4) 0 var(--space-2);
  transition: background var(--motion-fast) var(--motion-ease);
  display: grid;
  grid-template-columns: 38% minmax(0, 1fr);
  align-items: center;
  gap: var(--space-3);
}
.item:hover { background: var(--hover); }
.dim .item:not(:hover, .active, .related) .name, .dim .item:not(:hover, .active, .related) .track { opacity: .6; }
.item.active, .item.related { background: var(--blueprint-soft); box-shadow: inset 2px 0 0 var(--blueprint); }
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
.name strong { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: var(--text-strong); font-weight: var(--weight-semibold); }
.name small { grid-column: 2; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--dim); font-size: var(--text-micro); }

.track { position: relative; height: 18px; }
.selected-time { position: absolute; inset-block: 0; background: var(--blueprint-soft); border-inline: 1px solid var(--blueprint); }
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
/* A 6px tick is a hard tap target; the hit area extends past the paint. */
.tick::after { content: ""; position: absolute; inset: -5px -4px; }
.tick:not(:disabled):hover, .tick.active { outline: 2px solid var(--blueprint); outline-offset: 1px; }
.tick.testside { background: var(--muted); }
.tick.observed { background: var(--pt-cyan); }
.tick.applicationside { background: var(--blueprint); }
/* An inferred change is a guess, not a record: hollow where a recorded change is solid. */
.tick.inferred { background: transparent; border: 1px solid var(--muted); }
.tick.inferred.observed { border-color: var(--pt-cyan); }
.tick.inferred.applicationside { border-color: var(--blueprint); }
/* The cause in words, under the track that holds it: what changed, and the operation that did it. */
.cause { grid-column: 2; margin: 0; color: var(--muted); font-size: var(--text-micro); }

.legend { display: flex; flex-wrap: wrap; gap: var(--space-2) var(--space-4); color: var(--muted); font-size: var(--text-micro); }
.legend span { display: inline-flex; align-items: center; gap: var(--space-1); }
.legend .tick { position: static; width: 5px; height: 10px; margin: 0; }

@container (max-width: 560px) {
  .item { grid-template-columns: minmax(0, 1fr); gap: 0; padding-bottom: var(--space-2); }
  .scale { grid-template-columns: minmax(0, 1fr); padding-inline: var(--space-4) calc(var(--space-4) + var(--space-2)); }
  .ruler, .phases { grid-column: 1; }
  .track { margin: 0 var(--space-2); }
  .cause { grid-column: 1; }
}
</style>
