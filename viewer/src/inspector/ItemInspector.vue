<script setup lang="ts">
import { computed } from "vue";
import type { Change, Item, Span, TestTrace } from "../trace/model";
import { formatOffset, sourceLabels } from "../trace/format";
import JsonView from "./JsonView.vue";

/*
 * One tracked item: what it is now, how it got there, and which operations touched it. Each change reads the
 * way a shape check does - a key, the value it had crossed out, the value it got - so a trail of ten changes is
 * ten short diffs rather than ten full states.
 */
const props = defineProps<{ item: Item; test: TestTrace }>();
const emit = defineEmits<{ select: [span: Span] }>();

const state = computed(() => Object.entries(props.item.state));
const usedBy = computed(() => props.test.spans.filter(span => span.item === props.item));

interface Difference {
  key: string;
  before: string | null | undefined;
  after: string | null;
  /** First seen in this change, or changed from an earlier value. */
  added: boolean;
}

/**
 * What one change did to the state. A change's state can be partial, so a key it does not mention kept its
 * value: only keys it names are compared, and only those that differ are shown.
 */
function differences(change: Change, index: number): Difference[] {
  const known: Record<string, string | null> = {};
  for (const earlier of props.item.changes.slice(0, index)) Object.assign(known, earlier.state);
  return Object.entries(change.state)
    .filter(([key, value]) => !(key in known) || known[key] !== value)
    .map(([key, value]) => ({ key, before: known[key], after: value, added: !(key in known) }));
}

function isJson(value: string | null | undefined): boolean {
  const trimmed = value?.trim() ?? "";
  return trimmed.startsWith("{") || trimmed.startsWith("[");
}

function literal(value: string | null | undefined): string {
  return value === null || value === undefined ? "null" : value;
}
</script>

<template>
  <div class="item-inspector">
    <section class="block">
      <h3>State at the end</h3>
      <dl v-if="state.length" class="fields">
        <template v-for="[key, value] in state" :key="key">
          <dt>{{ key }}</dt>
          <dd>
            <JsonView v-if="isJson(value)" :value="value" :open-depth="0" />
            <template v-else>{{ literal(value) }}</template>
          </dd>
        </template>
      </dl>
      <p v-else class="empty">No state was recorded for this item.</p>
    </section>

    <section class="block">
      <h3>How it changed</h3>
      <ol class="trail">
        <li v-for="(change, index) in item.changes" :key="index">
          <span class="offset">{{ formatOffset(change.at - test.start) }}</span>
          <div class="what">
            <p>
              <strong>{{ change.change }}</strong>
              <span class="source" :class="change.source">{{ sourceLabels[change.source] }}</span>
              <em v-if="change.inferred">inferred</em>
            </p>
            <button v-if="change.span" type="button" class="by" @click="change.span && emit('select', change.span)">by {{ change.span.name }}</button>
            <div v-if="differences(change, index).length" class="diff">
              <div v-for="difference in differences(change, index)" :key="difference.key" class="difference" :class="{ added: difference.added }">
                <span class="mark" :aria-label="difference.added ? 'added' : 'changed'">{{ difference.added ? "+" : "~" }}</span>
                <span class="key">{{ difference.key }}</span>
                <span class="values">
                  <template v-if="isJson(difference.after)">
                    <JsonView :value="difference.after" :open-depth="0" />
                  </template>
                  <template v-else>
                    <code v-if="!difference.added" class="before">{{ literal(difference.before) }}</code>
                    <b v-if="!difference.added" class="arrow">→</b>
                    <code class="after">{{ literal(difference.after) }}</code>
                  </template>
                </span>
              </div>
            </div>
            <p v-else class="unchanged">No value changed.</p>
          </div>
        </li>
      </ol>
    </section>

    <section v-if="usedBy.length" class="block">
      <h3>Operations on it</h3>
      <button v-for="span in usedBy" :key="span.id" type="button" class="link-row" @click="emit('select', span)">
        <span>{{ span.name }}</span>
        <small>{{ formatOffset(span.start - test.start) }}</small>
      </button>
    </section>
  </div>
</template>

<style scoped>
.item-inspector { min-width: 0; display: grid; gap: var(--space-5); }
.source { padding: 0 var(--space-2); border-radius: var(--radius-chip); background: var(--surface-2); color: var(--muted); font-size: var(--text-micro); font-weight: var(--weight-semibold); }
.source.applicationside { background: var(--blueprint-soft); color: var(--blueprint); }
.source.observed { color: var(--pt-cyan); }

h3 { font-family: var(--font-ui); font-size: var(--text-meta); font-weight: var(--weight-bold); letter-spacing: 0; }
.block { min-width: 0; display: grid; gap: var(--space-2); }
.fields { margin: 0; display: grid; grid-template-columns: minmax(80px, max-content) minmax(0, 1fr); gap: 0 var(--space-4); }
.fields dt, .fields dd { min-width: 0; padding: var(--space-1) 0; border-top: 1px solid var(--border); }
.fields dt { color: var(--muted); font: var(--text-micro) var(--font-mono); overflow-wrap: anywhere; }
.fields dd { margin: 0; overflow-wrap: anywhere; font: var(--text-meta) var(--font-mono); }
.empty, .unchanged { color: var(--dim); font-size: var(--text-micro); }

.trail { margin: 0; padding: 0; list-style: none; display: grid; }
.trail li { position: relative; min-width: 0; padding: 0 0 var(--space-4) var(--space-5); display: grid; grid-template-columns: 64px minmax(0, 1fr); gap: var(--space-2); border-left: 1px solid var(--border); }
.trail li::before { content: ""; position: absolute; left: -4px; top: 3px; width: 7px; height: 7px; border-radius: 50%; background: var(--border-strong); }
.trail li:last-child { border-left-color: transparent; }
.offset { color: var(--muted); font-size: var(--text-micro); font-variant-numeric: tabular-nums; }
.what { min-width: 0; display: grid; gap: var(--space-1); }
.what p { display: flex; flex-wrap: wrap; align-items: center; gap: var(--space-2); font-size: var(--text-meta); }
.what em { color: var(--dim); font-size: var(--text-micro); }
.by { justify-self: start; padding: 0; border: 0; background: transparent; color: var(--blueprint); font-size: var(--text-micro); text-align: left; }
.by:hover { text-decoration: underline; }

/* The shape tree's language for a change: a mark, the key, the old value crossed out, the new one. */
.diff { min-width: 0; margin-top: var(--space-1); padding: var(--space-1) var(--space-2); display: grid; border: 1px solid var(--border); border-radius: var(--radius-chip); font: var(--text-micro)/var(--leading) var(--font-mono); }
.difference { min-width: 0; display: grid; grid-template-columns: 12px minmax(64px, max-content) minmax(0, 1fr); align-items: baseline; gap: var(--space-2); padding: 2px 0; }
.difference + .difference { border-top: 1px dashed var(--border); }
.mark { color: var(--blueprint); font-weight: var(--weight-bold); text-align: center; }
.added .mark { color: var(--success); }
.key { color: var(--blueprint); font-weight: var(--weight-semibold); overflow-wrap: anywhere; }
.values { min-width: 0; display: flex; flex-wrap: wrap; align-items: baseline; gap: var(--space-1); overflow-wrap: anywhere; }
.values > :deep(.json) { flex: 1 1 100%; }
.before { color: var(--muted); text-decoration: line-through; }
.arrow { color: var(--muted); font-weight: var(--weight-regular); }
.after { color: var(--text); font-weight: var(--weight-bold); }

.link-row { width: 100%; min-height: var(--row-height); padding: 0 var(--space-2); display: flex; align-items: center; justify-content: space-between; gap: var(--space-2); border: 1px solid transparent; border-radius: var(--radius-chip); background: var(--surface-2); font-size: var(--text-meta); text-align: left; transition: border-color var(--motion-fast) var(--motion-ease); }
.link-row:hover { border-color: var(--blueprint); }
.link-row small { color: var(--muted); font-size: var(--text-micro); }
</style>
