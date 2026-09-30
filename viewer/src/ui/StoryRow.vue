<script setup lang="ts">
import { computed } from "vue";
import type { Span, TestTrace } from "../trace/model";
import type { StoryRow } from "../trace/story";
import { formatDuration, formatOffset, itemKindLabel, kindLabel, outcomeLabel, plural, spanFacts, timelinePercent, tone } from "../trace/format";
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

const id = computed(() => props.row.type === "step" ? props.row.span.id : props.row.id);
const children = computed(() => props.row.type === "group" ? props.row.rows : props.row.type === "step" ? props.row.children : []);
const isOpen = computed(() => props.open.has(id.value));

/** Where the row ran inside the test, as a bar on the test's own clock: what took the time, without reading numbers. */
const range = computed(() => {
  const row = props.row;
  if (row.type === "gap") return { start: row.gap.start, end: row.gap.start + row.gap.duration };
  const spans = row.type === "group" ? row.spans : [row.span];
  return { start: Math.min(...spans.map(span => span.start)), end: Math.max(...spans.map(span => span.end)) };
});
const bar = computed(() => ({
  left: `${timelinePercent(range.value.start, props.test.start, props.test.duration)}%`,
  width: `${Math.max(0.8, ((range.value.end - range.value.start) / Math.max(props.test.duration, 1)) * 100)}%`
}));
const duration = computed(() => {
  const row = props.row;
  if (row.type === "gap") return row.gap.duration;
  return row.type === "group" ? row.spans.reduce((sum, span) => sum + span.duration, 0) : row.span.duration;
});

function countChanges(span: Span): number {
  return span.changes.length + span.children.reduce((sum, child) => sum + countChanges(child), 0);
}

/** What the row did, in facts: a call's status, else the state it changed - "Project created". */
const facts = computed(() => {
  const row = props.row;
  if (row.type === "gap") return "";
  if (row.type === "group") {
    const clients = row.spans.flatMap(span => [span, ...span.children]).filter(span => span.kind === "client.initialize").length;
    const changed = row.spans.reduce((sum, span) => sum + countChanges(span), 0);
    return [clients ? `${clients} ${plural(clients, "client")} initialized` : "", changed ? `${changed} ${plural(changed, "state change")}` : ""]
      .filter(Boolean).join(", ");
  }
  const own = spanFacts(row.span);
  // A status check on the row already says the status, with its verdict; the bare fact would repeat it.
  const statusChecked = row.checks.some(check => check.kind === "assert.http.status");
  if (own && !(statusChecked && own.startsWith("status "))) return own;
  const change = row.span.changes[0];
  if (!change) return "";
  const more = row.span.changes.length - 1;
  return `${itemKindLabel(change.item).label} ${change.change}${more ? `, and ${more} more` : ""}`;
});

const evidence = computed(() => props.row.type === "step" ? props.row.span.evidence : []);
const artifacts = computed(() => evidence.value.filter(item => item.type === "attachment").length);
const observations = computed(() => evidence.value.filter(item => item.type === "observation").length);
/* What the operation stated, in its own words: observations are deliberate facts, so the first reads inline. */
const firstObservation = computed(() => {
  for (const item of evidence.value) if (item.type === "observation") return item;
  return null;
});
const applicationSide = computed(() => props.row.type === "step" && props.row.span.changes.some(change => change.source === "applicationside"));

/** A gap says what the trace knows about it: where it ended, and what it could have been. */
const gapHint = computed(() => {
  const row = props.row;
  if (row.type !== "gap") return "";
  const gap = row.gap;
  if (gap.before) return `Until ${gap.before.name} started, ${formatOffset(gap.before.start - props.test.start)} into the test. A wait, or work the trace could not see.`;
  if (gap.lifecycle.children.length) return `Until the ${gap.phase} phase ended. A wait, or work the trace could not see.`;
  return `The ${gap.phase} phase recorded no operation at all. A call made outside the composition leaves none.`;
});

/** What the fold button folds: the step's name, or the framework group's label. */
const foldName = computed(() => props.row.type === "step" ? props.row.span.name : props.row.type === "group" ? props.row.label : "");

function pick() {
  if (props.row.type === "group") emit("toggle", id.value);
  else if (props.row.type === "step") emit("select", props.row.span);
}
</script>

<template>
  <div class="row" :class="{ nested: depth > 0 }">
    <div v-if="row.type === 'gap'" class="line gap">
      <span class="gap-mark" aria-hidden="true" />
      <p class="gap-text"><strong>{{ formatDuration(row.gap.duration) }} with no recorded operation</strong><small>{{ gapHint }}</small></p>
      <span class="checks" />
      <span class="marks" />
      <span class="waterfall" aria-hidden="true"><i :style="bar" /></span>
      <span class="duration">{{ formatDuration(duration) }}</span>
    </div>

    <div v-else class="line" :class="[row.type === 'step' ? tone(row.span.status) : 'group', { active: row.type === 'step' && row.span.id === selected }]">
      <button v-if="children.length" type="button" class="expand" :aria-expanded="isOpen"
              :aria-label="`${isOpen ? 'Fold' : 'Unfold'} ${foldName}`" @click="emit('toggle', id)">
        <svg viewBox="0 0 10 10" width="10" height="10" aria-hidden="true"><path d="M3.6 2 6.6 5 3.6 8" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" /></svg>
      </button>
      <span v-else class="leaf" aria-hidden="true" />

      <button type="button" class="pick" :data-span="row.type === 'step' ? row.span.id : undefined" :title="row.type === 'step' ? row.span.kind : row.spans.map(span => span.name).join('\n')" :aria-current="row.type === 'step' && row.span.id === selected ? 'true' : undefined" @click="pick">
        <KindChip :type="row.type === 'step' ? kindLabel(row.span.kind) : { id: 'extension', label: 'Framework' }" />
        <span class="title" :class="{ quiet: row.type === 'group' }">{{ row.type === "step" ? row.span.name : row.label }}</span>
        <span v-if="row.type === 'step' && row.span.count > 1" class="count" :title="`Ran ${row.span.count} times`">×{{ row.span.count }}</span>
        <span v-if="row.type === 'step'" class="visually-hidden">{{ outcomeLabel(row.span.status) }}</span>
        <span class="facts">
          <span v-if="applicationSide" class="app" title="Reported by the application itself">app</span>
          {{ facts }}
        </span>
      </button>

      <span v-if="row.type === 'step' && row.checks.length" class="checks">
        <button v-for="check in row.checks" :key="check.id" type="button" class="check" :data-span="check.id" :class="[tone(check.status), { active: check.id === selected }]"
                :title="check.name" :aria-current="check.id === selected ? 'true' : undefined" @click="emit('select', check)">
          <svg v-if="check.status === 'succeeded'" viewBox="0 0 10 10" width="9" height="9" aria-hidden="true"><path d="M2 5.3 4.1 7.4 8 2.8" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" /></svg>
          <svg v-else viewBox="0 0 10 10" width="9" height="9" aria-hidden="true"><path d="M2.6 2.6 7.4 7.4M7.4 2.6 2.6 7.4" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" /></svg>
          {{ check.name.replace(/^Assert\s+/i, "") }}
        </button>
      </span>
      <span v-else class="checks" />

      <span class="marks">
        <span v-if="artifacts" :title="`${artifacts} ${plural(artifacts, 'attachment')} captured by this operation`">{{ artifacts }} {{ plural(artifacts, "file") }}</span>
      </span>
      <span class="waterfall" aria-hidden="true"><i :style="bar" /></span>
      <span class="duration">{{ formatDuration(duration) }}</span>
    </div>

    <p v-if="row.type === 'step' && row.span.error && !row.span.kind.startsWith('test.')" class="error">
      {{ row.span.error.message.split(/\r?\n/)[0] }}
    </p>
    <p v-if="firstObservation" class="observed">
      Observed {{ firstObservation.kind }}<template v-if="firstObservation.identifier"> · {{ firstObservation.identifier }}</template> <span class="muted">on {{ firstObservation.target }}</span><span v-if="observations > 1">, and {{ observations - 1 }} more</span>
    </p>

    <template v-if="isOpen">
      <StoryRow v-for="child in children" :key="child.type === 'step' ? child.span.id : child.id" :row="child" :test="test"
                :depth="depth + 1" :selected="selected" :open="open"
                @select="emit('select', $event)" @toggle="emit('toggle', $event)" />
    </template>
  </div>
</template>

<style scoped>
.row.nested { margin-left: var(--space-3); padding-left: var(--space-3); border-left: 1px solid var(--border); }

/* One grid for every row at every depth, so checks, marks, bars and durations line up down the steps. */
.line {
  min-height: var(--control-height);
  transition: background var(--motion-fast) var(--motion-ease);
  display: grid;
  grid-template-columns: 18px minmax(0, 1fr) auto auto 120px 60px;
  align-items: center;
  gap: var(--space-2);
  border-radius: var(--radius-chip);
}
.line:hover { background: var(--hover); }
.line.active { background: var(--blueprint-soft); box-shadow: inset 2px 0 0 var(--blueprint); }
.line.danger { background: var(--danger-soft); box-shadow: inset 2px 0 0 var(--danger); }
.line.danger.active { box-shadow: inset 2px 0 0 var(--blueprint), inset 0 0 0 1px var(--blueprint); }

.expand {
  position: relative;
  width: 18px;
  height: 18px;
  padding: 0;
  display: grid;
  place-items: center;
  border: 0;
  border-radius: var(--radius-chip);
  background: transparent;
  color: var(--muted);
}
.expand svg { transition: transform var(--motion-fast) var(--motion-ease); }
.expand[aria-expanded="true"] svg { transform: rotate(90deg); }
/* An 18px box is a hard tap target; the hit area extends past the paint. */
.expand::after { content: ""; position: absolute; inset: -4px; }
.expand:hover { background: var(--surface-2); color: var(--text); }
.leaf { width: 18px; }

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
.title { flex: 0 1 auto; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: var(--text-strong); }
.title.quiet { color: var(--muted); }
.count { flex: none; color: var(--muted); font: var(--weight-semibold) var(--text-meta) var(--font-mono); }
.visually-hidden { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); }
.facts { flex: 1 1 0; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--muted); font-size: var(--text-body); }
/* Application-reported work is the deepest visibility a trace can have; it is marked where it happens. */
.app { margin-right: var(--space-1); padding: 0 var(--space-1); border-radius: var(--radius-hairline); background: var(--blueprint-soft); color: var(--blueprint); font-size: var(--text-meta); font-weight: var(--weight-semibold); }

.checks { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: var(--space-1); }
.check {
  height: 22px;
  padding: 0 var(--space-2);
  display: inline-flex;
  align-items: center;
  gap: var(--space-1);
  border: 1px solid var(--border);
  border-radius: var(--radius-pill);
  background: var(--surface);
  color: var(--muted);
  font-size: var(--text-meta);
  white-space: nowrap;
}
.check.success svg { color: var(--success); }
.check.danger { border-color: var(--danger); color: var(--danger); font-weight: var(--weight-semibold); }
.check.warning { border-color: var(--warning); color: var(--warning); }
.check:hover { border-color: var(--blueprint); color: var(--text); }
.check.active { border-color: var(--blueprint); background: var(--blueprint-soft); color: var(--text); }

.marks { display: flex; justify-content: flex-end; gap: var(--space-2); color: var(--dim); font-size: var(--text-meta); white-space: nowrap; }
.waterfall { position: relative; height: 6px; border-radius: var(--radius-hairline); background: var(--surface-2); }
.waterfall i { position: absolute; top: 0; bottom: 0; min-width: 2px; border-radius: var(--radius-hairline); background: var(--muted); }
.line.danger .waterfall i { background: var(--danger); }
.line.group .waterfall i { background: var(--border-strong); }
.duration { color: var(--muted); font-size: var(--text-body); text-align: right; font-variant-numeric: tabular-nums; }

/* Untraced time: hatched where solid would be a recorded operation. It is stated, never left as empty space. */
.gap { min-height: 40px; }
.gap:hover { background: transparent; }
.gap-mark, .gap .waterfall i {
  background: repeating-linear-gradient(135deg, color-mix(in srgb, var(--muted) 55%, transparent) 0 2px, transparent 2px 5px);
}
.gap-mark { width: 14px; height: 10px; justify-self: center; border-radius: var(--radius-hairline); }
.gap-text { min-width: 0; padding: 0 var(--space-1); display: grid; gap: 1px; }
.gap-text strong { font-size: var(--text-body); font-weight: var(--weight-semibold); }
.gap-text small { color: var(--muted); font-size: var(--text-meta); }
.gap .duration { color: var(--text); font-weight: var(--weight-semibold); }

.error { margin: 1px 0 var(--space-1) var(--space-7); color: var(--danger); font-size: var(--text-body); }
/* A stated fact reads where it happened; the full value stays one click away in the inspector. */
.observed { margin: 1px 0 var(--space-1) var(--space-7); color: var(--text); font-size: var(--text-body); overflow-wrap: anywhere; }
.observed .muted { color: var(--muted); font-size: var(--text-meta); }

@container (max-width: 760px) {
  .marks { display: none; }
  .line { grid-template-columns: 18px minmax(0, 1fr) auto 88px 56px; }
}
@container (max-width: 560px) {
  .line { grid-template-columns: 18px minmax(0, 1fr) 56px; row-gap: 0; padding-block: var(--space-1); }
  .checks { grid-column: 2 / -1; justify-content: flex-start; }
  .checks:empty, .marks, .waterfall { display: none; }
  .duration { grid-row: 1; grid-column: 3; }
}
/* Very narrow: the name is what identifies a row; its facts are one tap away in the details. */
@container (max-width: 420px) {
  .facts { display: none; }
}
</style>
