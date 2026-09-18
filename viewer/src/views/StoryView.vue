<script setup lang="ts">
import { computed, nextTick, ref, watch } from "vue";
import type { Span, TestTrace } from "../trace/model";
import { pathTo, story } from "../trace/story";
import type { StoryRow as Row } from "../trace/story";
import { formatDuration } from "../trace/format";
import StoryRow from "../ui/StoryRow.vue";
import OutcomePill from "../ui/OutcomePill.vue";
import EmptyState from "../ui/EmptyState.vue";
import Panel from "../ui/Panel.vue";

const props = defineProps<{ test: TestTrace; selected?: string }>();
const emit = defineEmits<{ select: [span: Span] }>();

const phases = computed(() => story(props.test));
const open = ref(new Set<string>());
const root = ref<HTMLElement>();

/** Steps open by default: the scenario reads top to bottom. Framework groups stay folded until asked. */
function defaults(): Set<string> {
  const ids = new Set<string>();
  const walk = (rows: Row[]) => rows.forEach(row => {
    if (row.type === "step") { if (row.children.length) ids.add(row.span.id); walk(row.children); }
  });
  phases.value.forEach(phase => walk(phase.rows));
  const failing = props.test.failure?.span.id;
  if (failing) pathTo(phases.value, failing).forEach(id => ids.add(id));
  return ids;
}

watch(() => props.test.id, () => { open.value = defaults(); }, { immediate: true });

// A selection made elsewhere - the failure card, the state view, a shared link - has to be visible here.
watch(() => props.selected, id => {
  if (!id) return;
  const next = new Set(open.value);
  pathTo(phases.value, id).slice(0, -1).forEach(step => next.add(step));
  open.value = next;
  // Unfolded, the row is in the document; bring it into view without jumping when it already is.
  void nextTick(() => root.value?.querySelector(".line.active, .check.active")?.scrollIntoView({ block: "nearest" }));
}, { immediate: true });

function phaseTitle(phase: string): string {
  return phase.charAt(0).toLocaleUpperCase() + phase.slice(1);
}

function toggle(id: string) {
  const next = new Set(open.value);
  if (next.has(id)) next.delete(id); else next.add(id);
  open.value = next;
}
</script>

<template>
  <div ref="root" class="story">
    <Panel v-for="phase in phases" :key="phase.phase" :title="phaseTitle(phase.phase)" pad="none" sticky
           :style="{ '--phase-color': `var(--phase-${phase.phase})` }">
      <template #lead><i class="marker" /></template>
      <template #actions>
        <OutcomePill v-if="phase.span && phase.span.status !== 'succeeded'" :outcome="phase.span.status" />
        <span class="duration">{{ phase.span ? formatDuration(phase.span.duration) : "" }}</span>
      </template>
      <div class="rows">
        <StoryRow v-for="row in phase.rows" :key="row.type === 'group' ? row.id : row.span.id" :row="row" :test="test"
                  :depth="0" :selected="selected" :open="open" @select="emit('select', $event)" @toggle="toggle" />
        <p v-if="!phase.rows.length" class="quiet">Nothing ran in this phase.</p>
      </div>
    </Panel>
    <EmptyState v-if="!phases.length" message="This test recorded no operations." />
  </div>
</template>

<style scoped>
.story { display: grid; gap: var(--space-4); container-type: inline-size; }
.marker { width: 8px; height: 8px; flex: none; border-radius: var(--radius-hairline); background: var(--phase-color); }
.duration { color: var(--muted); font-size: var(--text-meta); font-variant-numeric: tabular-nums; }
.rows { padding: var(--space-2) var(--space-2) var(--space-3); }
.quiet { padding: var(--space-2); color: var(--dim); font-size: var(--text-meta); }
</style>
