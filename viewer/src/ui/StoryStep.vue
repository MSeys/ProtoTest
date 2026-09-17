<script setup lang="ts">
import { computed } from "vue";
import type { TraceEntry } from "../model/trace-schema";
import type { StoryStep } from "../model/trace-rows";
import { entryTitle, formatDuration, milliseconds, tone } from "../model/trace-format";
import { evidenceRole, nodeFacts, nodeType } from "../model/trace-levels";
import KindChip from "./KindChip.vue";

const props = defineProps<{
  step: StoryStep;
  depth: number;
  selected?: TraceEntry;
  /** Steps the reader has folded away. */
  collapsed: Set<string>;
  /** Steps whose machinery the reader has opened. */
  opened: Set<string>;
}>();
const emit = defineEmits<{ select: [entry: TraceEntry]; collapse: [id: string]; toggle: [id: string] }>();

const isCollapsed = computed(() => props.collapsed.has(props.step.entry.id));
const hasItems = computed(() => props.step.items.length > 0);

/** What a folded step is hiding, so collapsing never hides how much there is. */
const summary = computed(() => {
  let operations = 0;
  let evidence = 0;
  const walk = (items: StoryStep["items"]) => {
    for (const item of items) {
      if (item.type === "evidence") { evidence += 1; continue; }
      operations += 1;
      walk(item.step.items);
    }
  };
  walk(props.step.items);
  const parts: string[] = [];
  if (operations) parts.push(`${operations} ${operations === 1 ? "operation" : "operations"}`);
  if (evidence) parts.push(`${evidence} ${evidence === 1 ? "result" : "results"}`);
  return parts.join(" · ");
});

function facts(entry: TraceEntry): string {
  return nodeFacts(entry).join(" · ");
}
</script>

<template>
  <div class="step" :class="{ nested: depth > 0 }">
    <div class="head" :class="[tone(step.entry.outcome), { active: step.entry.id === selected?.id }]">
      <button v-if="hasItems" type="button" class="expand" :aria-expanded="!isCollapsed"
              :aria-label="isCollapsed ? 'Expand step' : 'Collapse step'" @click="emit('collapse', step.entry.id)">
        <svg viewBox="0 0 10 10" width="10" height="10" aria-hidden="true">
            <path d="M1.6 5H8.4" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" />
            <path class="stem" d="M5 1.6V8.4" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" />
          </svg>
      </button>
      <span v-else class="node" :style="{ '--node-color': `var(--type-${nodeType(step.entry).id}, var(--type-custom))` }" aria-hidden="true" />

      <button type="button" class="pick" :title="step.entry.name" @click="emit('select', step.entry)">
        <KindChip :type="nodeType(step.entry)" />
        <span class="title">{{ entryTitle(step.entry) }}</span>
        <span class="facts mono" :class="{ summary: isCollapsed && summary }">{{ isCollapsed && summary ? summary : facts(step.entry) }}</span>
        <span class="duration mono">{{ step.entry.duration ? formatDuration(milliseconds(step.entry.duration)) : "" }}</span>
        <i class="status" :class="tone(step.entry.outcome)" />
      </button>
    </div>

    <p v-if="step.entry.error" class="failure">
      <strong class="mono">{{ step.entry.error.type }}</strong>{{ step.entry.error.message }}
    </p>

    <template v-if="!isCollapsed">
      <template v-for="item in step.items" :key="item.type === 'step' ? item.step.entry.id : item.entry.id">
        <StoryStep v-if="item.type === 'step'" :step="item.step" :depth="depth + 1" :selected="selected"
                   :collapsed="collapsed" :opened="opened"
                   @select="emit('select', $event)" @collapse="emit('collapse', $event)" @toggle="emit('toggle', $event)" />
        <button v-else type="button" class="evidence"
                :class="[tone(item.entry.outcome), { active: item.entry.id === selected?.id }]"
                :title="item.entry.name" @click="emit('select', item.entry)">
          <em>{{ evidenceRole(item.entry) }}</em>
          <span>{{ entryTitle(item.entry) }}</span>
          <small class="mono">{{ facts(item.entry) }}</small>
        </button>
      </template>

      <template v-if="step.machinery.length">
        <button type="button" class="machinery-toggle" :aria-expanded="opened.has(step.entry.id)"
                @click="emit('toggle', step.entry.id)">
          {{ opened.has(step.entry.id) ? "Hide" : "Show" }} {{ step.machinery.length }} internal
          {{ step.machinery.length === 1 ? "operation" : "operations" }}
        </button>
        <div v-if="opened.has(step.entry.id)" class="machinery">
          <button v-for="item in step.machinery" :key="item.id" type="button"
                  class="machinery-row" :class="{ active: item.id === selected?.id }" :title="item.name"
                  @click="emit('select', item)">
            <span>{{ entryTitle(item) }}</span>
            <small class="mono">{{ item.kind }}</small>
          </button>
        </div>
      </template>
    </template>
  </div>
</template>

<style scoped>
/* A nested step hangs off a drawn branch, the same language the tree speaks. */
.step.nested { margin-left: var(--space-2); padding-left: var(--space-4); border-left: 1px solid var(--border); }

.head {
  display: grid;
  grid-template-columns: 16px minmax(0, 1fr);
  align-items: center;
  gap: var(--space-1);
  border-radius: var(--radius-chip);
}
.head:hover { background: var(--hover); }
.head.active { background: var(--blueprint-soft); box-shadow: inset 2px 0 0 var(--blueprint); }
.head.danger { background: var(--danger-soft); box-shadow: inset 2px 0 0 var(--danger); }

.expand {
  width: 14px;
  height: 14px;
  display: grid;
  place-items: center;
  padding: 0;
  border: 1px solid var(--border-strong);
  border-radius: var(--radius-hairline);
  background: var(--surface);
  color: var(--muted);
}
.expand:hover { border-color: var(--blueprint); color: var(--text); }
.expand[aria-expanded="true"] .stem { opacity: 0; }
.expand .stem { transition: opacity var(--motion-fast) var(--motion-ease); }
.node {
  width: 7px;
  height: 7px;
  margin: 0 auto;
  border: 1px solid var(--node-color, var(--dim));
  background: var(--node-color, var(--dim));
  transform: rotate(45deg);
}

.pick {
  min-width: 0;
  min-height: 26px;
  padding: 0 var(--space-2);
  display: grid;
  grid-template-columns: auto minmax(0, 1.7fr) minmax(0, 1fr) 62px 7px;
  align-items: center;
  gap: var(--space-3);
  border: 0;
  background: transparent;
  text-align: left;
}
.title { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: var(--text-body); }
.facts { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--muted); font-size: var(--text-micro); }
.duration { text-align: right; white-space: nowrap; color: var(--muted); font-size: var(--text-micro); }

.failure {
  margin: 1px 0 1px var(--space-5);
  padding: var(--space-2) var(--space-3);
  border-left: 2px solid var(--danger);
  border-radius: 0 var(--radius-chip) var(--radius-chip) 0;
  background: var(--danger-soft);
  color: var(--muted);
  font-size: var(--text-meta);
}
.failure strong { margin-right: var(--space-2); color: var(--danger); font-size: var(--text-micro); }

.evidence {
  width: 100%;
  min-height: 24px;
  padding: 0 var(--space-2) 0 var(--space-5);
  display: grid;
  grid-template-columns: 64px minmax(0, 1.7fr) minmax(0, 1fr);
  align-items: center;
  gap: var(--space-3);
  border: 1px solid transparent;
  border-radius: var(--radius-chip);
  background: transparent;
  text-align: left;
}
.evidence:hover { background: var(--hover); }
.evidence.active { border-color: var(--blueprint); background: var(--blueprint-soft); }
.evidence em { color: var(--type-evidence); font: var(--weight-semibold) var(--text-micro) var(--font-mono); font-style: normal; }
.evidence.danger em { color: var(--danger); }
.evidence span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--muted); font-size: var(--text-meta); }
.evidence small { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--dim); font-size: var(--text-micro); }

.machinery-toggle {
  margin: 1px 0 1px var(--space-5);
  padding: 2px var(--space-2);
  border: 1px dashed var(--border-strong);
  border-radius: var(--radius-chip);
  background: transparent;
  color: var(--dim);
  font-size: var(--text-micro);
}
.machinery-toggle:hover { border-style: solid; border-color: var(--blueprint); color: var(--text); }
.machinery { margin: 1px 0 var(--space-2) var(--space-5); display: grid; }
.machinery-row {
  min-height: 22px;
  padding: 0 var(--space-2);
  display: grid;
  grid-template-columns: minmax(0, 1.7fr) minmax(0, 1fr);
  align-items: center;
  gap: var(--space-3);
  border: 1px solid transparent;
  border-radius: var(--radius-chip);
  background: transparent;
  text-align: left;
}
.machinery-row:hover { background: var(--hover); }
.machinery-row.active { border-color: var(--blueprint); background: var(--blueprint-soft); }
.machinery-row span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--dim); font-size: var(--text-meta); }
.machinery-row small { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--dim); font-size: var(--text-micro); }

/* Narrow: the facts column is the first thing to go; the name and the duration are what matter. */
/* Narrow: every column keeps its place so rows stay aligned; the text ellipses instead of wrapping. */
@container (max-width: 620px) {
  .pick { grid-template-columns: auto minmax(0, 1.2fr) minmax(0, 1fr) 54px 7px; gap: var(--space-2); }
  .evidence { grid-template-columns: 58px minmax(0, 1fr); }
  .machinery-row { grid-template-columns: minmax(0, 1fr); }
  .evidence small, .machinery-row small { display: none; }
}
</style>
