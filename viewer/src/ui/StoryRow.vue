<script setup lang="ts">
import { computed } from "vue";
import type { Span, TestTrace } from "../trace/model";
import type { StoryRow } from "../trace/story";
import { formatDuration, kindLabel, spanFacts, tone } from "../trace/format";
import KindChip from "./KindChip.vue";

const props = defineProps<{
  row: StoryRow;
  test: TestTrace;
  depth: number;
  selected?: string;
  /** Rows the reader has opened; a folded row keeps its place and says what it holds. */
  open: Set<string>;
}>();
const emit = defineEmits<{ select: [span: Span]; toggle: [id: string] }>();

const id = computed(() => props.row.type === "group" ? props.row.id : props.row.span.id);
const children = computed(() => props.row.type === "group" ? props.row.rows : props.row.children);
const isOpen = computed(() => props.open.has(id.value));

/** Where the row ran inside the test, as a thin bar: the reader sees what took the time without reading numbers. */
const spans = computed(() => props.row.type === "group" ? props.row.spans : [props.row.span]);
const start = computed(() => Math.min(...spans.value.map(span => span.start)));
const end = computed(() => Math.max(...spans.value.map(span => span.end)));
const duration = computed(() => props.row.type === "group"
  ? props.row.spans.reduce((sum, span) => sum + span.duration, 0)
  : props.row.span.duration);
const bar = computed(() => {
  const total = Math.max(props.test.duration, 1);
  return {
    left: `${Math.min(100, Math.max(0, ((start.value - props.test.start) / total) * 100))}%`,
    width: `${Math.max(0.8, ((end.value - start.value) / total) * 100)}%`
  };
});

/** What the row did, in facts: a call's status, else the state it changed - "Project created". */
const facts = computed(() => {
  if (props.row.type === "group") {
    const clients = props.row.spans.flatMap(span => [span, ...span.children]).filter(span => span.kind === "client.initialize").length;
    const changed = props.row.spans.reduce((sum, span) => sum + countChanges(span), 0);
    return [clients ? `${clients} client${clients === 1 ? "" : "s"} initialized` : "", changed ? `${changed} state change${changed === 1 ? "" : "s"}` : ""]
      .filter(Boolean).join(", ");
  }
  const own = spanFacts(props.row.span);
  // A status check on the row already says the status, with its verdict; the bare fact would repeat it.
  const statusChecked = props.row.checks.some(check => check.kind === "assert.http.status");
  if (own && !(statusChecked && own.startsWith("status "))) return own;
  const change = props.row.span.changes[0];
  if (!change) return "";
  const more = props.row.span.changes.length - 1;
  return `${change.item.kind} ${change.change}${more ? `, and ${more} more` : ""}`;
});

function countChanges(span: Span): number {
  return span.changes.length + span.children.reduce((sum, child) => sum + countChanges(child), 0);
}

const evidence = computed(() => props.row.type === "step" ? props.row.span.evidence : []);
const artifacts = computed(() => evidence.value.filter(item => item.type === "attachment").length);
const observations = computed(() => evidence.value.filter(item => item.type === "observation").length);
const applicationSide = computed(() => props.row.type === "step" && props.row.span.changes.some(change => change.source === "applicationside"));

function pick() {
  if (props.row.type === "group") emit("toggle", id.value);
  else emit("select", props.row.span);
}
</script>

<template>
  <div class="row" :class="{ nested: depth > 0 }">
    <div class="line" :class="[row.type === 'step' ? tone(row.span.status) : 'group', { active: row.type === 'step' && row.span.id === selected }]">
      <button v-if="children.length" type="button" class="expand" :aria-expanded="isOpen"
              :aria-label="isOpen ? 'Fold' : 'Unfold'" @click="emit('toggle', id)">
        <svg viewBox="0 0 10 10" width="10" height="10" aria-hidden="true">
          <path d="M1.6 5H8.4" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" />
          <path class="stem" d="M5 1.6V8.4" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" />
        </svg>
      </button>
      <span v-else class="node" aria-hidden="true"
            :style="{ '--node-color': `var(--type-${kindLabel(row.type === 'step' ? row.span.kind : 'hook.').id}, var(--type-custom))` }" />

      <button type="button" class="pick" :data-span="row.type === 'step' ? row.span.id : undefined" :title="row.type === 'step' ? row.span.kind : row.spans.map(span => span.name).join('\n')" @click="pick">
        <KindChip :type="row.type === 'step' ? kindLabel(row.span.kind) : { id: 'extension', label: 'Framework' }" />
        <span class="title" :class="{ quiet: row.type === 'group' }">{{ row.type === "step" ? row.span.name : row.label }}</span>
        <span class="facts">
          <span v-if="applicationSide" class="app" title="Reported by the application itself">app</span>
          {{ facts }}
        </span>
      </button>

      <span v-if="row.type === 'step' && row.checks.length" class="checks">
        <button v-for="check in row.checks" :key="check.id" type="button" class="check" :data-span="check.id" :class="[tone(check.status), { active: check.id === selected }]"
                :title="check.name" @click="emit('select', check)">
          <svg v-if="check.status === 'succeeded'" viewBox="0 0 10 10" width="9" height="9" aria-hidden="true"><path d="M2 5.3 4.1 7.4 8 2.8" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" /></svg>
          <svg v-else viewBox="0 0 10 10" width="9" height="9" aria-hidden="true"><path d="M2.6 2.6 7.4 7.4M7.4 2.6 2.6 7.4" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" /></svg>
          {{ check.name.replace(/^Assert\s+/i, "") }}
        </button>
      </span>
      <span v-else class="checks" />

      <span class="marks">
        <span v-if="artifacts" :title="`${artifacts} artifact${artifacts === 1 ? '' : 's'}`">⧉ {{ artifacts }}</span>
        <span v-if="observations" :title="`${observations} observation${observations === 1 ? '' : 's'}`">◎ {{ observations }}</span>
      </span>
      <span class="waterfall" aria-hidden="true"><i :style="bar" /></span>
      <span class="duration">{{ formatDuration(duration) }}</span>
    </div>

    <p v-if="row.type === 'step' && row.span.error && !row.span.kind.startsWith('test.')" class="error">
      {{ row.span.error.message.split(/\r?\n/)[0] }}
    </p>

    <template v-if="isOpen">
      <StoryRow v-for="child in children" :key="child.type === 'group' ? child.id : child.span.id" :row="child" :test="test"
                :depth="depth + 1" :selected="selected" :open="open"
                @select="emit('select', $event)" @toggle="emit('toggle', $event)" />
    </template>
  </div>
</template>

<style scoped>
.row.nested { margin-left: var(--space-2); padding-left: var(--space-3); border-left: 1px solid var(--border); }

/* One grid for every row at every depth, so checks, marks, bars and durations line up down the story. */
.line {
  min-height: var(--row-height);
  transition: background var(--motion-fast) var(--motion-ease);
  display: grid;
  grid-template-columns: 16px minmax(0, 1fr) auto 44px 72px 56px;
  align-items: center;
  gap: var(--space-2);
  border-radius: var(--radius-chip);
}
.line:hover { background: var(--hover); }
.line.active { background: var(--blueprint-soft); box-shadow: inset 2px 0 0 var(--blueprint); }
.line.danger { background: var(--danger-soft); box-shadow: inset 2px 0 0 var(--danger); }

.expand {
  width: 14px;
  height: 14px;
  padding: 0;
  display: grid;
  place-items: center;
  border: 1px solid var(--border-strong);
  border-radius: var(--radius-hairline);
  background: var(--surface);
  color: var(--muted);
}
.expand:hover { border-color: var(--blueprint); color: var(--text); }
.expand[aria-expanded="true"] .stem { opacity: 0; }
.node { width: 7px; height: 7px; margin: 0 auto; background: var(--node-color, var(--dim)); transform: rotate(45deg); }

.pick {
  min-width: 0;
  padding: 0 var(--space-1);
  display: flex;
  align-items: center;
  gap: var(--space-2);
  border: 0;
  background: transparent;
  text-align: left;
}
.title { flex: 0 1 auto; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: var(--text-body); }
.title.quiet { color: var(--muted); }
.facts { flex: 1 1 0; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--muted); font-size: var(--text-micro); }
/* Application-reported work is the deepest visibility a trace can have; it is marked where it happens. */
.app { margin-right: var(--space-1); padding: 0 var(--space-1); border-radius: var(--radius-hairline); background: var(--blueprint-soft); color: var(--blueprint); font-weight: var(--weight-semibold); }

.checks { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: var(--space-1); }
.check {
  height: 20px;
  padding: 0 var(--space-2);
  display: inline-flex;
  align-items: center;
  gap: var(--space-1);
  border: 1px solid var(--border);
  border-radius: var(--radius-pill);
  background: var(--surface);
  color: var(--muted);
  font-size: var(--text-micro);
  white-space: nowrap;
}
.check.success svg { color: var(--success); }
.check.danger { border-color: var(--danger); color: var(--danger); font-weight: var(--weight-semibold); }
.check.warning { border-color: var(--warning); color: var(--warning); }
.check:hover { border-color: var(--blueprint); color: var(--text); }
.check.active { border-color: var(--blueprint); background: var(--blueprint-soft); color: var(--text); }

.marks { display: flex; justify-content: flex-end; gap: var(--space-2); color: var(--dim); font-size: var(--text-micro); white-space: nowrap; }
.waterfall { position: relative; height: 4px; border-radius: var(--radius-hairline); background: var(--surface-2); }
.waterfall i { position: absolute; top: 0; bottom: 0; min-width: 2px; border-radius: var(--radius-hairline); background: var(--muted); }
.line.danger .waterfall i { background: var(--danger); }
.line.group .waterfall i { background: var(--border-strong); }
.duration { color: var(--muted); font-size: var(--text-micro); text-align: right; font-variant-numeric: tabular-nums; }

.error { margin: 1px 0 var(--space-1) var(--space-6); color: var(--danger); font-size: var(--text-meta); }

@container (max-width: 720px) {
  .line { grid-template-columns: 16px minmax(0, 1fr) 56px; row-gap: 0; padding-block: var(--space-1); }
  .checks { grid-column: 2 / -1; justify-content: flex-start; }
  .checks:empty, .marks, .waterfall { display: none; }
  .duration { grid-row: 1; grid-column: 3; }
}
/* Very narrow: the name is what identifies a row; its facts are one tap away in the inspector. */
@container (max-width: 460px) {
  .facts { display: none; }
}
</style>
