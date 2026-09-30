<script setup lang="ts">
import { computed, nextTick, ref, watch } from "vue";
import type { Span, TestTrace } from "../trace/model";
import { fails, isFramework, pathTo, story, withoutFramework } from "../trace/story";
import { frameworkMode } from "../ui/frameworkMode";
import type { StoryPhase, StoryRow as Row } from "../trace/story";
import { formatDuration, phaseSummary } from "../trace/format";
import StoryRow from "../ui/StoryRow.vue";
import OutcomePill from "../ui/OutcomePill.vue";
import EmptyState from "../ui/EmptyState.vue";
import LooseEvents from "../ui/LooseEvents.vue";
import { looseEvents } from "../ui/looseEvents";

/*
 * What the test did, phase by phase. The test body is open; setup and teardown are one line each that says
 * what they did, and open by themselves along a failure. Time the trace could not account for has a row of
 * its own, where it happened.
 */
const props = defineProps<{ test: TestTrace; selected?: string }>();
const emit = defineEmits<{ select: [span: Span] }>();

const phases = computed(() => story(props.test));
/** What the rows show: everything, or with the framework left out when the reader hid it. */
const visible = (phase: StoryPhase) => frameworkMode.value === "hide" ? withoutFramework(phase.rows) : phase.rows;
const hasLoose = computed(() => looseEvents(props.test).length > 0);
const open = ref(new Set<string>());
const root = ref<HTMLElement>();

const phaseId = (phase: StoryPhase) => `phase:${phase.phase}`;

/** A row the reader would call part of the scenario: not framework machinery, not the auth a call applies. */
function scenario(row: Row): boolean {
  return row.type === "group" || (row.type === "step" && !isFramework(row.span) && !row.span.kind.startsWith("auth."));
}

/** Open by default: the body, any phase that went wrong, steps with scenario work inside, and the failure's path. */
function defaults(): Set<string> {
  const ids = new Set<string>();
  const walk = (rows: Row[]) => rows.forEach(row => {
    if (row.type !== "step") return;
    if (row.children.some(scenario)) ids.add(row.span.id);
    walk(row.children);
  });
  for (const phase of phases.value) {
    const failing = (phase.span && phase.span.status !== "succeeded") || phase.rows.some(row => row.type === "step" ? fails(row.span) : row.type === "group" && row.spans.some(fails));
    if (phase.phase === "execution" || phase.phase === "rollback" || failing) ids.add(phaseId(phase));
    walk(phase.rows);
  }
  const failure = props.test.failure?.span;
  if (failure) {
    ids.add(`phase:${failure.phase}`);
    pathTo(phases.value, failure.id).slice(0, -1).forEach(id => ids.add(id));
  }
  return ids;
}

watch(() => props.test.id, () => { open.value = defaults(); }, { immediate: true });

// A selection made elsewhere - the verdict, the state, a shared link - has to be visible here.
watch(() => props.selected, id => {
  if (!id) return;
  const span = props.test.byId.get(id);
  const next = new Set(open.value);
  if (span) next.add(`phase:${span.phase}`);
  pathTo(phases.value, id).slice(0, -1).forEach(step => next.add(step));
  open.value = next;
  // Unfolded, the row is in the document; bring it into view without jumping when it already is.
  void nextTick(() => root.value?.querySelector(".line.active, .check.active")?.scrollIntoView({ block: "nearest" }));
}, { immediate: true });

function toggle(id: string) {
  const next = new Set(open.value);
  if (next.has(id)) next.delete(id); else next.add(id);
  open.value = next;
}

function phaseTitle(phase: string): string {
  return phase.charAt(0).toLocaleUpperCase() + phase.slice(1);
}
</script>

<template>
  <div ref="root" class="steps">
    <section v-for="phase in phases" :key="phase.phase" class="phase" :class="{ open: open.has(phaseId(phase)) }"
             :style="{ '--phase-color': `var(--phase-${phase.phase})` }">
      <button type="button" class="phase-head" :aria-expanded="open.has(phaseId(phase))" @click="toggle(phaseId(phase))">
        <svg class="chevron" viewBox="0 0 10 10" width="10" height="10" aria-hidden="true"><path d="M3.6 2 6.6 5 3.6 8" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" /></svg>
        <i class="marker" aria-hidden="true" />
        <strong>{{ phaseTitle(phase.phase) }}</strong>
        <span class="summary">{{ phase.span ? phaseSummary(phase.span) : "" }}</span>
        <OutcomePill v-if="phase.span && phase.span.status !== 'succeeded'" :outcome="phase.span.status" />
        <span class="duration">{{ phase.span ? formatDuration(phase.span.duration) : "" }}</span>
      </button>
      <div v-if="open.has(phaseId(phase))" class="rows">
        <StoryRow v-for="row in visible(phase)" :key="row.type === 'step' ? row.span.id : row.id" :row="row" :test="test"
                  :depth="0" :selected="selected" :open="open" @select="emit('select', $event)" @toggle="toggle" />
        <p v-if="!visible(phase).length" class="quiet">{{ phase.rows.length ? "Only framework operations ran in this phase." : "Nothing ran in this phase." }}</p>
      </div>
    </section>
    <EmptyState v-if="!phases.length && !hasLoose" message="This test recorded no operations." />
    <LooseEvents :test="test" />
  </div>
</template>

<style scoped>
.steps { display: grid; gap: var(--space-3); container-type: inline-size; }
.phase { border: 1px solid var(--border); border-radius: var(--radius-panel); background: var(--surface); overflow: clip; }
/* The phase head stays in view while its rows scroll under it, below the view tabs. */
.phase-head {
  position: sticky;
  top: var(--sticky-offset, 0px);
  z-index: 1;
  width: 100%;
  min-height: 44px;
  padding: var(--space-2) var(--space-4);
  display: grid;
  grid-template-columns: 10px 8px auto minmax(0, 1fr) auto auto;
  align-items: center;
  gap: var(--space-3);
  border: 0;
  background: var(--surface);
  text-align: left;
}
.phase-head:hover { background: var(--hover); }
.phase.open .phase-head { border-bottom: 1px solid var(--border); }
.chevron { color: var(--muted); transition: transform var(--motion-fast) var(--motion-ease); }
.phase.open .chevron { transform: rotate(90deg); }
.marker { width: 8px; height: 8px; border-radius: var(--radius-hairline); background: var(--phase-color); }
.phase-head strong { font-size: var(--text-strong); }
.summary { min-width: 0; overflow: hidden; color: var(--muted); font-size: var(--text-body); text-overflow: ellipsis; white-space: nowrap; }
.duration { color: var(--muted); font-size: var(--text-body); font-variant-numeric: tabular-nums; }
.rows { padding: var(--space-2) var(--space-2) var(--space-3); }
.quiet { padding: var(--space-2); color: var(--dim); font-size: var(--text-body); }

@container (max-width: 520px) {
  .phase-head { grid-template-columns: 10px 8px minmax(0, 1fr) auto auto; }
  .summary { display: none; }
}
</style>
