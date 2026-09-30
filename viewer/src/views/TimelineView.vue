<script setup lang="ts">
import { computed, nextTick, ref, watch } from "vue";
import type { Item, Phase, Span, TestTrace } from "../trace/model";
import { untracedGaps, type UntracedGap } from "../trace/analysis";
import { isFramework } from "../trace/story";
import { formatDuration, formatOffset, kindLabel, outcomeLabel, plural, rulerTicks, tone } from "../trace/format";
import KindChip from "../ui/KindChip.vue";
import FilterChip from "../ui/FilterChip.vue";
import TextInput from "../ui/TextInput.vue";
import EmptyState from "../ui/EmptyState.vue";

/*
 * Every operation the test recorded, on the test's own clock. Where Steps folds and summarizes, this list
 * keeps everything: search answers where an operation is, the filter what needs attention, the zoom one
 * phase at a time. Framework machinery is dimmed, or hidden on request; a hit always keeps its ancestors.
 */
const props = defineProps<{ test: TestTrace; selected?: string; selectedItem?: Item }>();
const emit = defineEmits<{ select: [span: Span] }>();
function related(span: Span) {
  return Boolean(props.selectedItem && (span.item === props.selectedItem || span.changes.some(change => change.item === props.selectedItem)));
}

const query = ref("");
const attention = ref(false);
const framework = ref<"dim" | "hide">("dim");
const zoom = ref<Phase | "test">("test");
const folded = ref(new Set<string>());
watch(() => props.test.id, () => { folded.value = new Set(); attention.value = false; query.value = ""; zoom.value = "test"; });

const lifecycles = computed(() => props.test.roots.filter(span => span.kind === `test.${span.phase}`));
const zoomed = computed(() => lifecycles.value.find(span => span.phase === zoom.value) ?? null);
/** The window the ruler spans: the whole test, or one phase. */
const frame = computed(() => zoomed.value
  ? { start: zoomed.value.start, length: Math.max(zoomed.value.duration, 1e-3) }
  : { start: props.test.start, length: Math.max(props.test.duration, ...props.test.spans.map(span => span.end - props.test.start), 1e-3) });

/** What needs attention here: a span that failed, stopped short, or carries an error of its own. */
function needsSpan(span: Span): boolean {
  return span.status === "failed" || span.status === "cancelled" || span.status === "partial" || Boolean(span.error);
}
const attentionCount = computed(() => props.test.spans.filter(needsSpan).length);
const machinery = (span: Span) => isFramework(span) || span.kind.startsWith("auth.");
/** Only machinery all the way down, with nothing that went wrong: what hiding the framework hides. */
function pureMachinery(span: Span): boolean {
  return machinery(span) && !needsSpan(span) && span.children.every(pureMachinery);
}

const hits = computed(() => {
  const text = query.value.trim().toLocaleLowerCase();
  if (!text) return null;
  return new Set(props.test.spans
    .filter(span => [span.name, span.kind, span.source, ...Object.values(span.attributes)].join(" ").toLocaleLowerCase().includes(text))
    .map(span => span.id));
});
const flagged = computed(() => attention.value ? new Set(props.test.spans.filter(needsSpan).map(span => span.id)) : null);
/** The spans the reader asked for, matching every active criterion, with their ancestors for context. */
const shown = computed(() => {
  if (!hits.value && !flagged.value) return null;
  const base = hits.value && flagged.value ? new Set([...hits.value].filter(id => flagged.value!.has(id))) : (hits.value ?? flagged.value!);
  const keep = new Set<string>();
  for (const id of base) for (let current = props.test.byId.get(id) ?? null; current; current = current.parent) keep.add(current.id);
  return { base, keep };
});

type Entry = { type: "span"; span: Span; depth: number } | { type: "gap"; gap: UntracedGap; depth: number };
const gaps = computed(() => untracedGaps(props.test));
/** The gaps inside the zoomed window, drawn through every row. */
const zones = computed(() => gaps.value.filter(gap => gap.start + gap.duration > frame.value.start && gap.start < frame.value.start + frame.value.length));
const entries = computed(() => {
  const result: Entry[] = [];
  const withGaps = !shown.value;
  const walk = (spans: Span[], depth: number) => spans.forEach(span => {
    if (shown.value && !shown.value.keep.has(span.id)) return;
    if (zoomed.value && span.phase !== zoomed.value.phase) return;
    if (framework.value === "hide" && pureMachinery(span)) return;
    if (withGaps) gaps.value.filter(gap => gap.before === span).forEach(gap => result.push({ type: "gap", gap, depth }));
    result.push({ type: "span", span, depth });
    if (!folded.value.has(span.id) || shown.value) walk(span.children, depth + 1);
    if (withGaps && span.kind === `test.${span.phase}`) {
      gaps.value.filter(gap => !gap.before && gap.lifecycle === span).forEach(gap => result.push({ type: "gap", gap, depth: depth + 1 }));
    }
  });
  walk(props.test.roots, 0);
  return result;
});
const spanRows = computed(() => entries.value.filter(entry => entry.type === "span").length);

/** The count names what is filtered, so a short list never reads as the whole tree. */
const count = computed(() => {
  if (shown.value && hits.value && flagged.value) return `${shown.value.base.size} of ${flagged.value.size} flagged ${shown.value.base.size === 1 ? "match" : "matches"}`;
  if (hits.value) return `${hits.value.size} of ${props.test.spans.length} matches`;
  if (flagged.value) return `${flagged.value.size} of ${props.test.spans.length} need attention`;
  return "";
});
const emptyMessage = computed(() => {
  if (!props.test.spans.length) return "This test recorded no operations.";
  if (hits.value && flagged.value) return "No flagged operation matches this search.";
  if (hits.value) return "No operation matches this search.";
  return "No operation needs attention.";
});

const percent = (at: number) => Math.min(100, Math.max(0, ((at - frame.value.start) / frame.value.length) * 100));
function place(start: number, duration: number) {
  const left = percent(start);
  const right = percent(start + duration);
  return { left: `${left}%`, width: `max(2px, ${Math.max(0, right - left)}%)` };
}
const ticks = computed(() => rulerTicks(0, frame.value.length).map(value => ({ value, left: `${percent(frame.value.start + value)}%` })));
const phaseMarks = computed(() => (zoomed.value ? [zoomed.value] : lifecycles.value).map(span => ({ span, ...place(span.start, span.duration) })));

/** What a span left behind, in the model's own words. */
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
  return parts.join(", ");
}
function family(span: Span): string {
  if (span.status === "failed" || span.error) return "failed";
  if (span.kind.startsWith("assert.")) return "check";
  if (machinery(span) || span.kind.startsWith("test.")) return "machinery";
  return "work";
}

// A selection made elsewhere scrolls its row into view, and a zoom that hides it steps back to the whole test.
const list = ref<HTMLElement>();
watch(() => props.selected, id => {
  const span = id ? props.test.byId.get(id) : undefined;
  if (span && zoomed.value && span.phase !== zoomed.value.phase) zoom.value = "test";
  void nextTick(() => list.value?.querySelector(".row.active")?.scrollIntoView({ block: "nearest" }));
}, { immediate: true });

function toggle(id: string) {
  const next = new Set(folded.value);
  if (next.has(id)) next.delete(id); else next.add(id);
  folded.value = next;
}
function reset() { query.value = ""; attention.value = false; }

// Pressing / anywhere outside a field lands in the search: the list is long and the search is its index.
function onKey(event: KeyboardEvent) {
  if (event.key !== "/" || event.altKey || event.ctrlKey || event.metaKey) return;
  const target = event.target as HTMLElement | null;
  if (target?.closest("input, textarea, select, [contenteditable]")) return;
  const input = (event.currentTarget as HTMLElement | null)?.querySelector<HTMLInputElement>("input[type='search']");
  if (!input) return;
  event.preventDefault();
  input.focus();
}
const title = (phase: string) => phase.charAt(0).toUpperCase() + phase.slice(1);
</script>

<template>
  <div class="timeline" @keydown="onKey">
    <div class="tools">
      <TextInput v-model="query" type="search" placeholder="Find by name, kind or attribute" label="Find an operation"
                 title="Find an operation (press / to focus)" class="find" />
      <div class="group" role="group" aria-label="Show operations">
        <FilterChip label="All" :count="test.spans.length" :active="!attention" @select="attention = false" />
        <FilterChip label="Needs attention" :count="attentionCount" :tone="attentionCount ? 'danger' : 'neutral'" :active="attention" @select="attention = true" />
      </div>
      <div class="group" role="group" aria-label="Framework operations">
        <span>Framework</span>
        <FilterChip label="Dim" :active="framework === 'dim'" @select="framework = 'dim'" />
        <FilterChip label="Hide" :active="framework === 'hide'" @select="framework = 'hide'" />
      </div>
      <div class="group" role="group" aria-label="Zoom to">
        <span>Zoom</span>
        <FilterChip label="Whole test" :active="zoom === 'test'" @select="zoom = 'test'" />
        <FilterChip v-for="span in lifecycles" :key="span.id" :label="title(span.phase)" :active="zoom === span.phase" @select="zoom = span.phase" />
      </div>
      <span v-if="count" class="count" role="status">{{ count }}</span>
    </div>

    <section class="panel">
      <div class="head" aria-hidden="true">
        <span>{{ spanRows }} of {{ test.spans.length }} operations<template v-if="zoomed">, from {{ formatOffset(zoomed.start - test.start) }}</template></span>
        <span class="ruler">
          <span v-for="mark in phaseMarks" :key="mark.span.id" class="phase" :style="{ left: mark.left, width: mark.width, '--phase-color': `var(--phase-${mark.span.phase})` }">{{ title(mark.span.phase) }}</span>
          <span v-for="tick in ticks" :key="tick.value" class="tick" :style="{ left: tick.left }">{{ tick.value === 0 ? "0" : formatOffset(tick.value) }}</span>
        </span>
        <span class="duration-head">Duration</span>
      </div>
      <div v-if="entries.length" ref="list" class="list" role="list" aria-label="Every operation">
        <template v-for="entry in entries" :key="entry.type === 'span' ? entry.span.id : `gap-${entry.gap.phase}-${entry.gap.start}`">
          <div v-if="entry.type === 'gap'" class="row gap" role="listitem" :style="{ '--depth': entry.depth }">
            <span class="label"><span class="fold" /><span class="gap-name">{{ formatDuration(entry.gap.duration) }} with no recorded operation</span></span>
            <span class="track"><i class="bar untraced" :style="place(entry.gap.start, entry.gap.duration)" /></span>
            <span class="duration">{{ formatDuration(entry.gap.duration) }}</span>
          </div>
          <div v-else class="row" role="listitem" :style="{ '--depth': entry.depth }"
               :class="[tone(entry.span.status), { active: entry.span.id === selected, related: related(entry.span), dim: framework === 'dim' && machinery(entry.span) && !needsSpan(entry.span) && !related(entry.span) }]">
            <span class="label">
              <button v-if="entry.span.children.length && !shown" type="button" class="fold" :aria-expanded="!folded.has(entry.span.id)"
                      :aria-label="`${folded.has(entry.span.id) ? 'Unfold' : 'Fold'} ${entry.span.name}`" @click="toggle(entry.span.id)">
                <svg viewBox="0 0 10 10" width="10" height="10" aria-hidden="true"><path d="M3.6 2 6.6 5 3.6 8" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" /></svg>
              </button>
              <span v-else class="fold" />
              <button type="button" class="pick" :data-span="entry.span.id" :aria-current="entry.span.id === selected ? 'true' : undefined"
                      :title="`${entry.span.name}\n${entry.span.kind} from ${entry.span.source}, ${formatOffset(entry.span.start - test.start)} into the test`"
                      @click="emit('select', entry.span)">
                <KindChip :type="kindLabel(entry.span.kind)" />
                <span class="name">{{ entry.span.name }}</span>
                <span v-if="marks(entry.span)" class="events">{{ marks(entry.span) }}</span>
                <span class="visually-hidden">{{ outcomeLabel(entry.span.status) }}</span>
              </button>
            </span>
            <span class="track">
              <i v-for="gap in zones" :key="`z${gap.start}`" class="zone" :style="place(gap.start, gap.duration)" aria-hidden="true" />
              <i class="bar" :class="family(entry.span)" :style="place(entry.span.start, entry.span.duration)" />
              <i v-for="(moment, index) in entry.span.moments" :key="`m${index}`" class="moment" :style="{ left: `${percent(moment.at)}%` }" :title="moment.name" />
              <i v-for="(item, index) in entry.span.evidence" :key="`e${index}`" class="evidence" :style="{ left: `${percent(item.at)}%` }" />
            </span>
            <span class="duration">{{ formatDuration(entry.span.duration) }}</span>
          </div>
        </template>
      </div>
      <EmptyState v-else :message="emptyMessage">
        <FilterChip v-if="hits || flagged" label="Show all operations" @select="reset" />
      </EmptyState>
    </section>
    <p class="legend" aria-hidden="true">
      <span><i class="work" />Work the test did</span><span><i class="check" />Check</span><span><i class="machinery" />Framework</span>
      <span><i class="failed" />Failed</span><span><i class="untraced" />No recorded operation</span>
      <span><i class="moment-key" />Moment</span><span><i class="evidence-key" />Evidence</span>
    </p>
  </div>
</template>

<style scoped>
.timeline { --label: minmax(220px, 44%); display: grid; gap: var(--space-3); container-type: inline-size; }
.tools { display: flex; flex-wrap: wrap; align-items: center; gap: var(--space-2) var(--space-4); }
.find { width: min(100%, 280px); }
.group { display: flex; flex-wrap: wrap; align-items: center; gap: var(--space-1); color: var(--dim); font-size: var(--text-meta); }
.group > span { margin-right: var(--space-1); }
.count { color: var(--muted); font-size: var(--text-meta); white-space: nowrap; }
.panel { border: 1px solid var(--border); border-radius: var(--radius-panel); background: var(--surface); overflow: clip; }
.head, .row { display: grid; grid-template-columns: var(--label) minmax(0, 1fr) 64px; align-items: center; gap: var(--space-3); }
/* The ruler stays under the view tabs while the rows scroll. */
.head {
  position: sticky;
  top: var(--sticky-offset, 0px);
  z-index: 2;
  padding: var(--space-2) var(--space-4) var(--space-1) var(--space-3);
  border-bottom: 1px solid var(--border);
  background: var(--surface);
  color: var(--muted);
  font-size: var(--text-meta);
}
.ruler { position: relative; height: 30px; }
.ruler .phase { position: absolute; top: 0; height: 14px; padding-left: var(--space-1); overflow: hidden; border-left: 2px solid var(--phase-color); color: var(--muted); font-size: var(--text-meta); font-weight: var(--weight-semibold); white-space: nowrap; }
.ruler .tick { position: absolute; bottom: 0; transform: translateX(-50%); color: var(--dim); font: var(--text-meta) var(--font-mono); white-space: nowrap; }
.ruler .tick:first-of-type { transform: none; }
.duration-head { text-align: right; }
.list { position: relative; padding: var(--space-1) var(--space-4) var(--space-3) var(--space-3); }
/* Untraced time runs through every row as a hatched band, the way it runs through the test. */
.zone { position: absolute; top: -6px; bottom: -6px; opacity: .16; pointer-events: none; }
.zone, .bar.untraced, .legend .untraced {
  background: repeating-linear-gradient(135deg, color-mix(in srgb, var(--muted) 60%, transparent) 0 2px, transparent 2px 5px);
}
.row { position: relative; min-height: var(--control-height); border-radius: var(--radius-chip); }
.row:hover { background: var(--hover); }
.row.active, .row.related { background: var(--blueprint-soft); box-shadow: inset 2px 0 0 var(--blueprint); }
.row.danger .name { color: var(--danger); }
.row.dim .label, .row.dim .duration { opacity: .6; }
.row.dim .bar { opacity: .45; }
.label { min-width: 0; padding-left: calc(var(--depth) * 14px); display: flex; align-items: center; gap: var(--space-1); }
.fold { width: 18px; height: 18px; flex: none; padding: 0; display: grid; place-items: center; border: 0; border-radius: var(--radius-chip); background: transparent; color: var(--muted); }
button.fold:hover { background: var(--surface-2); color: var(--text); }
button.fold svg { transition: transform var(--motion-fast) var(--motion-ease); }
button.fold[aria-expanded="true"] svg { transform: rotate(90deg); }
.pick { min-width: 0; padding: 0; display: flex; align-items: center; gap: var(--space-2); border: 0; background: transparent; text-align: left; }
.name { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: var(--text-body); }
.events { flex: none; color: var(--dim); font-size: var(--text-meta); }
.gap-name { color: var(--text); font-size: var(--text-body); font-weight: var(--weight-semibold); }
.track { position: relative; height: 16px; }
.bar { position: absolute; top: 4px; height: 8px; border-radius: var(--radius-hairline); }
.bar.work { background: var(--type-action); }
.bar.check { background: var(--type-evidence); }
.bar.machinery { background: var(--border-strong); }
.bar.failed { background: var(--danger); }
.moment { position: absolute; top: 1px; width: 1px; height: 14px; background: var(--text); opacity: .5; }
.evidence { position: absolute; top: 5px; width: 6px; height: 6px; transform: translateX(-50%); border-radius: 50%; background: var(--type-evidence); box-shadow: 0 0 0 1px var(--surface); }
.duration { color: var(--muted); font-size: var(--text-body); text-align: right; font-variant-numeric: tabular-nums; }
.visually-hidden { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); }
.legend { display: flex; flex-wrap: wrap; gap: var(--space-1) var(--space-4); color: var(--muted); font-size: var(--text-meta); }
.legend span { display: inline-flex; align-items: center; gap: var(--space-1); }
.legend i { width: 12px; height: 8px; border-radius: var(--radius-hairline); }
.legend .work { background: var(--type-action); }
.legend .check { background: var(--type-evidence); }
.legend .machinery { background: var(--border-strong); }
.legend .failed { background: var(--danger); }
.legend .moment-key { width: 1px; height: 12px; background: var(--text); opacity: .5; }
.legend .evidence-key { width: 6px; height: 6px; border-radius: 50%; background: var(--type-evidence); }

@container (max-width: 820px) {
  .events, .pick :deep(.chip) { display: none; }
}
@container (max-width: 640px) {
  .timeline { --label: minmax(140px, 45%); }
}
</style>
