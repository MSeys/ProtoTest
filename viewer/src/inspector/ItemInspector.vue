<script setup lang="ts">
import { computed } from "vue";
import type { Change, Item, Run, Span, TestTrace } from "../trace/model";
import { formatOffset, sourceLabels } from "../trace/format";
import JsonView from "./JsonView.vue";
import { isJsonLike } from "./json";
import DetailCard from "../ui/DetailCard.vue";
import PropertyList from "../ui/PropertyList.vue";

/*
 * One tracked item: what it holds at the end, how it got there, and which operations touched it. The keys
 * share a namespace ("resource."), so it is said once above them; values read as the text they are. A change
 * shows only what it changed, one line per value, and the change that created the item folds its first values
 * away, because the table above already holds them.
 */
const props = defineProps<{ item: Item; test: TestTrace | Run }>();
const emit = defineEmits<{ select: [span: Span] }>();

const usedBy = computed(() => props.test.spans.filter(span => span.item === props.item));

/** The namespace every key shares, with its dot, or nothing when the keys do not agree on one. */
const namespace = computed(() => {
  const keys = [...Object.keys(props.item.state), ...props.item.changes.flatMap(change => Object.keys(change.state))];
  const first = keys[0]?.split(".")[0];
  return first && keys.every(key => key.startsWith(`${first}.`)) ? `${first}.` : "";
});
const shortKey = (key: string) => namespace.value && key.startsWith(namespace.value) ? key.slice(namespace.value.length) : key;

/** A value as text: no JSON quotes around a string, a unit where the key names one. */
function display(key: string, value: string | null | undefined): string {
  if (value === null || value === undefined) return "null";
  return /(_ms|Ms)$/.test(key) && value !== "" && !Number.isNaN(Number(value)) ? `${value} ms` : value;
}

const state = computed(() => Object.entries(props.item.state).map(([key, value]) => ({ key, label: shortKey(key), value })));
const stateMeta = computed(() => [namespace.value.slice(0, -1), `${state.value.length} ${state.value.length === 1 ? "property" : "properties"}`].filter(Boolean).join(" · "));

interface Difference {
  key: string;
  label: string;
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
    .map(([key, value]) => ({ key, label: shortKey(key), before: known[key], after: value, added: !(key in known) }));
}

/** The first change that only set values: it reads as one line, its values folded. */
const opening = (change: Change, index: number) => index === 0 && differences(change, index).every(difference => difference.added);
</script>

<template>
  <div class="item-inspector">
    <DetailCard v-if="state.length" title="State at the end" :meta="stateMeta" plain>
      <PropertyList :entries="state">
        <template #value="{ entry }">
          <JsonView v-if="isJsonLike(entry.value)" :value="entry.value" :open-depth="0" />
          <template v-else>{{ display(entry.key, entry.value) }}</template>
        </template>
      </PropertyList>
    </DetailCard>
    <p v-else class="empty">No state was recorded for this item.</p>

    <section class="block">
      <h3>How it changed</h3>
      <ol class="trail">
        <li v-for="(change, index) in item.changes" :key="index" :class="change.source">
          <span class="offset">{{ formatOffset(change.at - test.start) }}</span>
          <div class="what">
            <p class="line">
              <strong>{{ change.change }}</strong>
              <span class="by">
                {{ sourceLabels[change.source] }}<template v-if="change.inferred">, inferred</template><template v-if="change.span">, by
                  <button type="button" :title="change.span.name" @click="change.span && emit('select', change.span)">{{ change.span.name }}</button></template>
              </span>
            </p>
            <details v-if="opening(change, index)" class="values">
              <summary>with {{ differences(change, index).length }} {{ differences(change, index).length === 1 ? "value" : "values" }}</summary>
              <dl class="diff">
                <template v-for="difference in differences(change, index)" :key="difference.key">
                  <dt :title="difference.key">{{ difference.label }}</dt>
                  <dd><span class="after">{{ display(difference.key, difference.after) }}</span></dd>
                </template>
              </dl>
            </details>
            <dl v-else-if="differences(change, index).length" class="diff">
              <template v-for="difference in differences(change, index)" :key="difference.key">
                <dt :title="difference.key">{{ difference.label }}</dt>
                <dd>
                  <JsonView v-if="isJsonLike(difference.after)" :value="difference.after" :open-depth="0" />
                  <template v-else>
                    <template v-if="!difference.added"><s class="before">{{ display(difference.key, difference.before) }}</s><span class="arrow" aria-label="became">→</span></template>
                    <span class="after">{{ display(difference.key, difference.after) }}</span>
                    <small v-if="difference.added" class="new">new</small>
                  </template>
                </dd>
              </template>
            </dl>
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
.block { min-width: 0; display: grid; gap: var(--space-2); }
h3 { color: var(--muted); font-family: var(--font-ui); font-size: var(--text-meta); font-weight: var(--weight-semibold); letter-spacing: 0; }

/* A change's values: the short key, then what it was and what it became. */
.diff { margin: 0; display: grid; grid-template-columns: fit-content(40%) minmax(0, 1fr); column-gap: var(--space-4); }
.empty, .unchanged { color: var(--dim); font-size: var(--text-meta); }

/* The trail: one dot per change on a thin line, the time beside it, and what changed under its name. */
.trail { margin: 0; padding: 0; list-style: none; display: grid; }
.trail li { position: relative; min-width: 0; padding: 0 0 var(--space-4) var(--space-4); display: grid; grid-template-columns: 56px minmax(0, 1fr); gap: var(--space-2); border-left: 1px solid var(--border); }
.trail li::before { content: ""; position: absolute; left: -4px; top: 5px; width: 7px; height: 7px; border-radius: 50%; background: var(--border-strong); }
.trail li.applicationside::before { background: var(--blueprint); }
.trail li:last-child { padding-bottom: 0; border-left-color: transparent; }
.offset { padding-top: 1px; color: var(--muted); font-size: var(--text-micro); font-variant-numeric: tabular-nums; }
.what { min-width: 0; display: grid; gap: var(--space-1); }
.line { display: flex; flex-wrap: wrap; align-items: baseline; gap: 0 var(--space-2); font-size: var(--text-meta); }
.line strong { font-weight: var(--weight-semibold); }
.by { min-width: 0; color: var(--dim); font-size: var(--text-micro); }
.by button { max-width: 100%; padding: 0; border: 0; background: transparent; color: var(--muted); font-size: var(--text-micro); text-align: left; }
.by button:hover { color: var(--text); text-decoration: underline; }

.diff dt, .diff dd { min-width: 0; padding: 1px 0; font-size: var(--text-micro); overflow-wrap: anywhere; }
.diff dt { color: var(--muted); }
.diff dd { margin: 0; display: flex; flex-wrap: wrap; align-items: baseline; gap: 0 var(--space-1); }
.diff dd > :deep(.card) { flex: 1 1 100%; }
.before { color: var(--dim); }
.arrow { color: var(--dim); }
.after { color: var(--text); font-weight: var(--weight-semibold); }
.new { color: var(--success); font-size: var(--text-micro); }

.values > summary { width: max-content; color: var(--muted); font-size: var(--text-micro); cursor: pointer; }
.values > summary:hover { color: var(--text); }
.values[open] > summary { margin-bottom: var(--space-1); }

.link-row { width: 100%; min-height: var(--row-height); padding: 0 var(--space-2); display: flex; align-items: center; justify-content: space-between; gap: var(--space-2); border: 0; border-radius: var(--radius-chip); background: transparent; font-size: var(--text-meta); text-align: left; transition: background var(--motion-fast) var(--motion-ease); }
.link-row:hover { background: var(--hover); }
.link-row small { color: var(--muted); font-size: var(--text-micro); }
</style>
