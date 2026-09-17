<script setup lang="ts">
import { computed, ref, watch } from "vue";
import type { TestTrace, TraceEntry } from "../model/trace-schema";
import { countStoryItems, primaryFailure, storyRows } from "../model/trace-rows";
import type { StoryItem } from "../model/trace-rows";
import { entryTitle, formatDuration, tone } from "../model/trace-format";
import { evidenceRole, nodeFacts } from "../model/trace-levels";
import Panel from "../ui/Panel.vue";
import AppButton from "../ui/AppButton.vue";
import StoryStep from "../ui/StoryStep.vue";
import OutcomePill from "../ui/OutcomePill.vue";
import EmptyState from "../ui/EmptyState.vue";

const props = defineProps<{ test: TestTrace; selected?: TraceEntry }>();
const emit = defineEmits<{ select: [entry: TraceEntry] }>();
const opened = ref(new Set<string>());
const collapsed = ref(new Set<string>());

const phases = computed(() => storyRows(props.test));
const shown = computed(() => phases.value.reduce((sum, phase) => sum + countStoryItems(phase.items), 0));

/** Every step that carries something, so 'collapse all' and the default fold have one source. */
const foldable = computed(() => {
  const ids: string[] = [];
  const walk = (items: StoryItem[]) => {
    for (const item of items) {
      if (item.type !== "step") continue;
      if (item.step.items.length) ids.push(item.step.entry.id);
      walk(item.step.items);
    }
  };
  for (const phase of phases.value) walk(phase.items);
  return ids;
});

/** The ancestors of the failure a reader opened the test for; those stay open. */
const failurePath = computed(() => {
  const ids = new Set<string>();
  const byId = new Map(props.test.entries.map(entry => [entry.id, entry]));
  let current = primaryFailure(props.test);
  while (current) {
    ids.add(current.id);
    current = current.parentId ? byId.get(current.parentId) : undefined;
  }
  return ids;
});

// A test opens folded: the shape of the run first, the detail on request — except on the way to a failure.
watch(() => props.test.testId, () => {
  collapsed.value = new Set(foldable.value.filter(id => !failurePath.value.has(id)));
}, { immediate: true });

// A selection made elsewhere — the failure banner, the tree, a shared link — has to be visible here.
watch(() => props.selected?.id, id => {
  if (!id) return;
  const byId = new Map(props.test.entries.map(entry => [entry.id, entry]));
  const next = new Set(collapsed.value);
  let current = byId.get(id);
  while (current) {
    next.delete(current.id);
    current = current.parentId ? byId.get(current.parentId) : undefined;
  }
  collapsed.value = next;
});

function collapse(id: string) {
  const next = new Set(collapsed.value);
  if (next.has(id)) next.delete(id); else next.add(id);
  collapsed.value = next;
}
function toggle(id: string) {
  const next = new Set(opened.value);
  if (next.has(id)) next.delete(id); else next.add(id);
  opened.value = next;
}
</script>

<template>
  <Panel title="Story" subtitle="What happened, in order. Plumbing folds onto the step that owns it; a failure never folds." pad="none">
    <template #actions>
      <AppButton variant="quiet" @click="collapsed.size ? (collapsed = new Set()) : (collapsed = new Set(foldable))">
        {{ collapsed.size ? "Expand all" : "Collapse all" }}
      </AppButton>
      <span class="count mono">{{ shown }} of {{ test.entries.length }} entries</span>
    </template>

    <div v-if="phases.length" class="phases">
      <section v-for="phase in phases" :key="phase.phase" class="phase"
               :style="{ '--phase-color': `var(--phase-${phase.phase.toLocaleLowerCase()})` }">
        <header>
          <span class="marker" />
          <strong>{{ phase.phase }}</strong>
          <small class="mono">{{ formatDuration(phase.summary.duration) }}</small>
          <OutcomePill :outcome="phase.summary.outcome" />
        </header>

        <div class="steps">
          <template v-for="item in phase.items" :key="item.type === 'step' ? item.step.entry.id : item.entry.id">
            <StoryStep v-if="item.type === 'step'" :step="item.step" :depth="0" :selected="selected"
                       :collapsed="collapsed" :opened="opened"
                       @select="emit('select', $event)" @collapse="collapse" @toggle="toggle" />
            <button v-else type="button" class="loose"
                    :class="[tone(item.entry.outcome), { active: item.entry.id === selected?.id }]"
                    :title="item.entry.name" @click="emit('select', item.entry)">
              <em>{{ evidenceRole(item.entry) }}</em>
              <span>{{ entryTitle(item.entry) }}</span>
              <small class="mono">{{ nodeFacts(item.entry).join(" · ") }}</small>
            </button>
          </template>

          <p v-if="!phase.items.length" class="quiet">
            Only framework machinery ran in this phase. The Tree has every entry.
          </p>
        </div>
      </section>
    </div>
    <EmptyState v-else message="This test recorded no trace entries." />
  </Panel>
</template>

<style scoped>
.count { color: var(--muted); font-size: var(--text-micro); }
.phases { padding: 0 var(--space-3) var(--space-4); container-type: inline-size; }
.phase + .phase { margin-top: var(--space-5); }
.phase > header {
  position: sticky;
  top: 0;
  z-index: 1;
  padding: var(--space-2);
  display: grid;
  grid-template-columns: 8px minmax(0, 1fr) auto auto;
  align-items: center;
  gap: var(--space-3);
  border-bottom: 1px solid var(--border);
  background: var(--surface);
}
.phase > header strong { color: var(--muted); font-size: var(--text-micro); letter-spacing: .08em; text-transform: uppercase; }
.phase > header small { color: var(--dim); font-size: var(--text-micro); }
.marker { width: 8px; height: 8px; border-radius: var(--radius-hairline); background: var(--phase-color, var(--dim)); }
.steps { padding-top: var(--space-2); }
.loose {
  width: 100%;
  min-height: 24px;
  padding: 0 var(--space-2);
  display: grid;
  grid-template-columns: 64px minmax(0, 1.7fr) minmax(0, 1fr);
  align-items: center;
  gap: var(--space-3);
  border: 1px solid transparent;
  border-radius: var(--radius-chip);
  background: transparent;
  text-align: left;
}
.loose:hover { background: var(--hover); }
.loose.active { border-color: var(--blueprint); background: var(--blueprint-soft); }
.loose em { color: var(--type-evidence); font: var(--weight-semibold) var(--text-micro) var(--font-mono); font-style: normal; }
.loose.danger em { color: var(--danger); }
.loose span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--muted); font-size: var(--text-meta); }
.loose small { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--dim); font-size: var(--text-micro); }
.quiet { padding: var(--space-3) var(--space-2); color: var(--dim); font-size: var(--text-meta); }

@container (max-width: 620px) {
  .loose { grid-template-columns: 64px minmax(0, 1fr); }
  .loose small { display: none; }
}
</style>
