<script setup lang="ts">
import { computed, nextTick, ref, watch } from "vue";
import type { Span, TestTrace } from "../trace/model";
import { formatDuration, formatOffset, kindLabel, outcomeLabel, plural, tone } from "../trace/format";
import KindChip from "../ui/KindChip.vue";
import FilterChip from "../ui/FilterChip.vue";
import TextInput from "../ui/TextInput.vue";
import EmptyState from "../ui/EmptyState.vue";
import Panel from "../ui/Panel.vue";

/*
 * Every span, as recorded, in one indented list: the complete fallback when the story's folding hides
 * something. Events on a span are listed under it. The search answers where a span is; the filter answers
 * what needs attention. Either one keeps a match's ancestors, so a hit is never shown out of context.
 */
const props = defineProps<{ test: TestTrace; selected?: string }>();
const emit = defineEmits<{ select: [span: Span] }>();

const query = ref("");
const filter = ref<"all" | "attention">("all");
const folded = ref(new Set<string>());
watch(() => props.test.id, () => { folded.value = new Set(); filter.value = "all"; query.value = ""; });

/** Spans that match the search themselves; ancestors are kept so a hit is never shown out of context. */
const hits = computed(() => {
  const text = query.value.trim().toLocaleLowerCase();
  if (!text) return null;
  const found = new Set<string>();
  for (const span of props.test.spans) {
    const haystack = [span.name, span.kind, span.source, ...Object.values(span.attributes)].join(" ").toLocaleLowerCase();
    if (haystack.includes(text)) found.add(span.id);
  }
  return found;
});

/** What needs attention here: a span that failed, stopped short, or carries an error of its own. */
function needsSpan(span: Span): boolean {
  return span.status === "failed" || span.status === "cancelled" || span.status === "partial" || Boolean(span.error);
}

const flagged = computed(() => {
  if (filter.value !== "attention") return null;
  const found = new Set<string>();
  for (const span of props.test.spans) {
    if (needsSpan(span)) found.add(span.id);
  }
  return found;
});

const attentionCount = computed(() => props.test.spans.filter(needsSpan).length);

/** The spans the reader asked for: matching every active criterion, ancestors kept for context. */
const shown = computed(() => {
  if (!hits.value && !flagged.value) return null;
  const base = hits.value && flagged.value
    ? new Set([...hits.value].filter(id => flagged.value!.has(id)))
    : (hits.value ?? flagged.value!);
  const keep = new Set<string>();
  for (const id of base) {
    for (let current: Span | null = props.test.byId.get(id) ?? null; current; current = current.parent) keep.add(current.id);
  }
  return { base, keep };
});

const rows = computed(() => {
  const result: Span[] = [];
  const walk = (spans: Span[]) => spans.forEach(span => {
    if (shown.value && !shown.value.keep.has(span.id)) return;
    result.push(span);
    if (!folded.value.has(span.id) || shown.value) walk(span.children);
  });
  walk(props.test.roots);
  return result;
});

/** The count names what is filtered, so a short list never reads as the whole tree. */
const count = computed(() => {
  if (shown.value && hits.value && flagged.value) return `${shown.value.base.size} of ${flagged.value.size} flagged ${shown.value.base.size === 1 ? "match" : "matches"}`;
  if (hits.value) return `${hits.value.size} of ${props.test.spans.length} matches`;
  if (flagged.value) return `${flagged.value.size} of ${props.test.spans.length} need attention`;
  return "";
});

/** An empty list is either an empty trace or a filter with nothing to show; only one of those is news. */
const emptyMessage = computed(() => {
  if (!props.test.spans.length) return "This test recorded no spans.";
  if (hits.value && flagged.value) return "No flagged span matches this search.";
  if (hits.value) return "No span matches this search.";
  return "No span needs attention.";
});

/** What a span left behind, in the model's own words: observations stated it, attachments it captured. */
function marks(span: Span): string {
  const observations = span.evidence.filter(item => item.type === "observation").length;
  const attachments = span.evidence.filter(item => item.type === "attachment").length;
  const findings = span.evidence.filter(item => item.type === "finding").length;
  const moments = span.moments.length;
  const parts: string[] = [];
  if (observations) parts.push(`${observations} ${plural(observations, "observation")}`);
  if (attachments) parts.push(`${attachments} ${plural(attachments, "attachment")}`);
  if (findings) parts.push(`${findings} ${plural(findings, "finding")}`);
  if (moments) parts.push(`${moments} ${plural(moments, "moment")}`);
  return parts.join(" · ");
}

// A selection made elsewhere scrolls its row into view, so the tree never hides what the details show.
const list = ref<HTMLElement>();
watch(() => props.selected, () => void nextTick(() => list.value?.querySelector(".row.active")?.scrollIntoView({ block: "nearest" })), { immediate: true });

function toggle(id: string) {
  const next = new Set(folded.value);
  if (next.has(id)) next.delete(id); else next.add(id);
  folded.value = next;
}

// Pressing / anywhere outside a field lands in the search: the tree is long and the search is its index.
function onKey(event: KeyboardEvent) {
  if (event.key !== "/" || event.altKey || event.ctrlKey || event.metaKey) return;
  const target = event.target as HTMLElement | null;
  if (target?.closest("input, textarea, select, [contenteditable]")) return;
  const input = (event.currentTarget as HTMLElement | null)?.querySelector<HTMLInputElement>("input[type='search']");
  if (!input) return;
  event.preventDefault();
  input.focus();
}
</script>

<template>
  <Panel class="spans" title="Every span" :subtitle="`${test.spans.length} spans as recorded, in the order they started.`" pad="none" @keydown="onKey">
    <template #actions>
      <span v-if="count" class="count" role="status">{{ count }}</span>
      <FilterChip label="All" :count="test.spans.length" :active="filter === 'all'" @select="filter = 'all'" />
      <FilterChip label="Needs attention" :count="attentionCount" :tone="attentionCount ? 'danger' : 'neutral'"
                  :active="filter === 'attention'" @select="filter = 'attention'" />
      <TextInput v-model="query" type="search" placeholder="Find by name, kind or attribute" label="Find a span"
                 title="Find a span (press / to focus)" class="find" />
    </template>
    <div v-if="rows.length" ref="list" class="list" role="list" aria-label="Every span">
      <div v-for="span in rows" :key="span.id" class="row" :class="[tone(span.status), { active: span.id === selected }]"
           role="listitem" :style="{ '--depth': span.depth }">
        <button v-if="span.children.length && !shown" type="button" class="fold" :aria-expanded="!folded.has(span.id)"
                :aria-label="`${folded.has(span.id) ? 'Unfold' : 'Fold'} ${span.name}`" @click="toggle(span.id)">
          <svg viewBox="0 0 10 10" width="10" height="10" aria-hidden="true">
            <path d="M1.6 5H8.4" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" />
            <path class="stem" d="M5 1.6V8.4" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" />
          </svg>
        </button>
        <span v-else class="fold" />
        <button type="button" class="pick" :data-span="span.id" :aria-current="span.id === selected ? 'true' : undefined" @click="emit('select', span)">
          <KindChip :type="kindLabel(span.kind)" />
          <span class="name">{{ span.name }}</span>
          <span class="kind">{{ span.kind }}</span>
          <span v-if="marks(span)" class="events">{{ marks(span) }}</span>
          <span class="visually-hidden">{{ outcomeLabel(span.status) }}</span>
        </button>
        <span class="offset">{{ formatOffset(span.start - test.start) }}</span>
        <span class="duration">{{ formatDuration(span.duration) }}</span>
        <i class="status" :class="tone(span.status)" aria-hidden="true" />
      </div>
    </div>
    <EmptyState v-else :message="emptyMessage">
      <FilterChip v-if="hits || flagged" label="Show all spans" @select="query = ''; filter = 'all'" />
    </EmptyState>
  </Panel>
</template>

<style scoped>
.spans { container-type: inline-size; }
.find { width: clamp(160px, 40%, 300px); }
.count { color: var(--muted); font-size: var(--text-micro); white-space: nowrap; }
.list { padding: var(--space-2); }
.row {
  min-height: var(--row-height);
  padding-left: calc(var(--depth) * var(--space-5));
  transition: background var(--motion-fast) var(--motion-ease);
  display: grid;
  grid-template-columns: 18px minmax(0, 1fr) 64px 56px 7px;
  align-items: center;
  gap: var(--space-2);
  border-radius: var(--radius-chip);
}
.row:hover { background: var(--hover); }
.row.active { background: var(--blueprint-soft); box-shadow: inset 2px 0 0 var(--blueprint); }
.row.danger { background: var(--danger-soft); }
.fold { width: 16px; height: 16px; padding: 0; display: grid; place-items: center; border: 0; background: transparent; color: var(--muted); }
button.fold {
  width: 14px;
  height: 14px;
  border: 1px solid var(--border-strong);
  border-radius: var(--radius-hairline);
  background: var(--surface);
  color: var(--muted);
}
button.fold:hover { border-color: var(--blueprint); color: var(--text); }
button.fold[aria-expanded="true"] .stem { opacity: 0; }
/* A 14px box is a hard tap target; the hit area extends past the paint. */
button.fold { position: relative; }
button.fold::after { content: ""; position: absolute; inset: -4px; }
.pick { min-width: 0; padding: 0; display: flex; align-items: center; gap: var(--space-2); border: 0; background: transparent; text-align: left; }
.visually-hidden { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); }
.name { flex: 0 1 auto; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: var(--text-meta); }
.kind { flex: 1 1 0; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--dim); font: var(--text-micro) var(--font-mono); }
.events { flex: none; color: var(--muted); font-size: var(--text-micro); }
.offset, .duration { color: var(--muted); font-size: var(--text-micro); text-align: right; font-variant-numeric: tabular-nums; }
/* The one marker in the column: only a failure paints, the way only a failing story row tints. */
.status { width: 0; }
.status.danger { width: 6px; height: 6px; border-radius: 50%; background: var(--danger); }

@container (max-width: 560px) {
  .row { grid-template-columns: 18px minmax(0, 1fr) 52px 7px; padding-left: calc(var(--depth) * 10px); }
  .offset, .kind { display: none; }
}
</style>
