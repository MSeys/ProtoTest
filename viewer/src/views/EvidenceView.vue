<script setup lang="ts">
import { computed, ref, watch } from "vue";
import type { Artifact, Evidence, Moment, Span, TestTrace } from "../trace/model";
import { formatBytes, formatOffset, plural, tone } from "../trace/format";
import FilterChip from "../ui/FilterChip.vue";
import EmptyState from "../ui/EmptyState.vue";
import JsonView from "../inspector/JsonView.vue";
import SectionView from "../inspector/SectionView.vue";
import PropertyList from "../ui/PropertyList.vue";

/*
 * Everything the test stated, captured, found or noted, in the order it happened, each with the operation
 * that recorded it: observations, files, findings and moments, including those with no operation above them.
 */
const props = defineProps<{ test: TestTrace }>();
const emit = defineEmits<{ select: [span: Span]; artifact: [artifact: Artifact] }>();

type Kind = "observation" | "file" | "finding" | "moment";
type Entry =
  | { kind: "observation"; at: number; item: Extract<Evidence, { type: "observation" }>; span: Span | null }
  | { kind: "file"; at: number; item: Extract<Evidence, { type: "attachment" }>; span: Span | null }
  | { kind: "finding"; at: number; item: Extract<Evidence, { type: "finding" }>; span: Span | null }
  | { kind: "moment"; at: number; item: Moment; span: Span | null };

function fromEvidence(item: Evidence): Entry {
  if (item.type === "observation") return { kind: "observation", at: item.at, item, span: item.span };
  if (item.type === "attachment") return { kind: "file", at: item.at, item, span: item.span };
  return { kind: "finding", at: item.at, item, span: item.span };
}

const entries = computed<Entry[]>(() => [
  ...props.test.spans.flatMap(span => span.evidence.map(fromEvidence)),
  ...props.test.spans.flatMap(span => span.moments.map((item): Entry => ({ kind: "moment", at: item.at, item, span }))),
  ...props.test.evidence.map(fromEvidence),
  ...props.test.moments.map((item): Entry => ({ kind: "moment", at: item.at, item, span: null }))
].sort((left, right) => left.at - right.at));

const filter = ref<Kind | "all">("all");
const open = ref(new Set<number>());
watch(() => props.test.id, () => { filter.value = "all"; open.value = new Set(); });

const labels: Record<Kind, [string, string]> = { observation: ["Observation", "Observations"], file: ["File", "Files"], finding: ["Finding", "Findings"], moment: ["Moment", "Moments"] };
const counts = computed(() => {
  const result: Record<Kind, number> = { observation: 0, file: 0, finding: 0, moment: 0 };
  entries.value.forEach(entry => { result[entry.kind] += 1; });
  return result;
});
const kinds = computed(() => (Object.keys(labels) as Kind[]).filter(kind => counts.value[kind] > 0 || filter.value === kind));
const shown = computed(() => entries.value.map((entry, index) => ({ entry, index })).filter(({ entry }) => filter.value === "all" || entry.kind === filter.value));

function title(entry: Entry): string {
  if (entry.kind === "observation") return entry.item.identifier ? `${entry.item.kind}, ${entry.item.identifier}` : entry.item.kind;
  if (entry.kind === "file") return entry.item.name;
  if (entry.kind === "finding") return entry.item.message;
  return entry.item.name;
}
function detail(entry: Entry): string {
  if (entry.kind === "observation") return `on ${entry.item.target}`;
  if (entry.kind === "file") {
    const artifact = entry.item.artifact;
    return artifact ? [artifact.mediaType, formatBytes(artifact.sizeBytes), artifact.description].filter(Boolean).join(", ") : "Declared with no file";
  }
  if (entry.kind === "finding") return [entry.item.status, entry.item.category, ...entry.item.tags].filter(Boolean).join(", ");
  const count = Object.keys(entry.item.attributes).length;
  return [entry.item.kind, count ? `${count} ${plural(count, "attribute")}` : ""].filter(Boolean).join(", ");
}
function expandable(entry: Entry): boolean {
  if (entry.kind === "observation") return Boolean(entry.item.data) || Object.keys(entry.item.metadata).length > 0;
  if (entry.kind === "finding") return Boolean(entry.item.category || entry.item.target || entry.item.tags.length || Object.keys(entry.item.metadata).length);
  if (entry.kind === "moment") return Boolean(entry.item.error || Object.keys(entry.item.attributes).length || entry.item.sections.length);
  return false;
}
function facts(entry: Entry): [string, string][] {
  const map: Record<string, string | null> =
    entry.kind === "observation" ? entry.item.metadata
      : entry.kind === "finding" ? { category: entry.item.category, tags: entry.item.tags.join(", ") || null, target: entry.item.target, ...entry.item.metadata }
        : entry.kind === "moment" ? entry.item.attributes : {};
  return Object.entries(map).filter((pair): pair is [string, string] => pair[1] !== null && pair[1] !== "");
}
function toggle(index: number) {
  const next = new Set(open.value);
  if (next.has(index)) next.delete(index); else next.add(index);
  open.value = next;
}
function entryTone(entry: Entry): string {
  if (entry.kind === "moment") return tone(entry.item.outcome);
  if (entry.kind === "finding") return entry.item.status.toLowerCase() === "error" ? "danger" : "warning";
  return "neutral";
}
</script>

<template>
  <div class="evidence">
    <div v-if="entries.length" class="tools" role="group" aria-label="Show evidence">
      <FilterChip label="All" :count="entries.length" :active="filter === 'all'" @select="filter = 'all'" />
      <FilterChip v-for="kind in kinds" :key="kind" :label="labels[kind][1]" :count="counts[kind]" :active="filter === kind" @select="filter = kind" />
    </div>
    <section v-if="shown.length" class="panel">
      <div v-for="{ entry, index } in shown" :key="index" class="entry" :class="[entry.kind, entryTone(entry)]">
        <div class="line">
          <span class="offset">{{ formatOffset(entry.at - test.start) }}</span>
          <span class="kind">{{ labels[entry.kind][0] }}</span>
          <component :is="expandable(entry) ? 'button' : 'span'" class="what" :type="expandable(entry) ? 'button' : undefined"
                     :aria-expanded="expandable(entry) ? open.has(index) : undefined" @click="expandable(entry) && toggle(index)">
            <strong>{{ title(entry) }}</strong>
            <small>{{ detail(entry) }}</small>
          </component>
          <button v-if="entry.kind === 'file' && entry.item.artifact" type="button" class="open" :disabled="Boolean(entry.item.artifact.error)"
                  @click="entry.item.artifact && emit('artifact', entry.item.artifact)">{{ entry.item.artifact.error ? "Unavailable" : "Open" }}</button>
          <span v-else />
          <button v-if="entry.span" type="button" class="from" :title="`Recorded by ${entry.span.name}`" @click="entry.span && emit('select', entry.span)">{{ entry.span.name }}</button>
          <span v-else class="from none">No operation above it</span>
        </div>
        <div v-if="open.has(index)" class="body">
          <pre v-if="entry.kind === 'moment' && entry.item.error" class="error">{{ entry.item.error.message }}</pre>
          <JsonView v-if="entry.kind === 'observation' && entry.item.data" :value="entry.item.data" :label="entry.item.identifier ?? entry.item.kind" :open-depth="1" />
          <PropertyList v-if="facts(entry).length" :entries="facts(entry).map(([key, value]) => ({ key, value }))" mono inline />
          <template v-if="entry.kind === 'moment'">
            <SectionView v-for="section in entry.item.sections" :key="section.label" :section="section" />
          </template>
        </div>
      </div>
    </section>
    <EmptyState v-else :message="entries.length ? 'Nothing of this kind was recorded.' : 'This test recorded no observations, files, findings or moments.'" />
  </div>
</template>

<style scoped>
.evidence { display: grid; gap: var(--space-3); container-type: inline-size; }
.tools { display: flex; flex-wrap: wrap; gap: var(--space-1); }
.panel { border: 1px solid var(--border); border-radius: var(--radius-panel); background: var(--surface); overflow: clip; }
.entry + .entry { border-top: 1px solid var(--border); }
.entry.danger { box-shadow: inset 2px 0 0 var(--danger); }
.entry.warning { box-shadow: inset 2px 0 0 var(--warning); }
.line { padding: var(--space-2) var(--space-4); display: grid; grid-template-columns: 64px 92px minmax(0, 1fr) auto minmax(0, 220px); align-items: center; gap: var(--space-3); }
.offset { color: var(--muted); font: var(--text-meta) var(--font-mono); font-variant-numeric: tabular-nums; }
.kind { justify-self: start; padding: 0 var(--space-2); border-radius: var(--radius-chip); background: var(--surface-2); color: var(--muted); font-size: var(--text-meta); font-weight: var(--weight-semibold); line-height: 1.8; }
.observation .kind { background: color-mix(in srgb, var(--type-evidence) 16%, transparent); color: color-mix(in srgb, var(--type-evidence) 78%, var(--text)); }
.file .kind { background: var(--blueprint-soft); color: var(--blueprint); }
.finding .kind { background: var(--warning-soft); color: var(--warning); }
.what { min-width: 0; padding: 0; display: grid; gap: 1px; border: 0; background: transparent; text-align: left; }
button.what:hover strong { text-decoration: underline; }
.what strong { overflow: hidden; font-size: var(--text-body); font-weight: var(--weight-semibold); text-overflow: ellipsis; white-space: nowrap; }
.what small { overflow: hidden; color: var(--muted); font-size: var(--text-meta); text-overflow: ellipsis; white-space: nowrap; }
.open { height: 24px; padding: 0 var(--space-3); border: 1px solid var(--border-strong); border-radius: var(--radius-control); background: var(--surface-2); font-size: var(--text-meta); }
.open:hover:not(:disabled) { border-color: var(--blueprint); }
.from { min-width: 0; justify-self: end; max-width: 100%; padding: 2px var(--space-2); overflow: hidden; border: 1px solid var(--border); border-radius: var(--radius-chip); background: transparent; color: var(--muted); font-size: var(--text-meta); text-align: left; text-overflow: ellipsis; white-space: nowrap; }
button.from:hover { border-color: var(--blueprint); color: var(--text); }
.from.none { border-style: dashed; color: var(--dim); }
.body { padding: 0 var(--space-4) var(--space-3) calc(64px + 92px + var(--space-4) + var(--space-3) * 2); display: grid; gap: var(--space-2); }
.error { margin: 0; white-space: pre-wrap; overflow-wrap: anywhere; color: var(--danger); font: var(--text-meta)/var(--leading) var(--font-mono); }

@container (max-width: 680px) {
  .line { grid-template-columns: 56px minmax(0, 1fr) auto; }
  .kind { grid-column: 2; grid-row: 1; }
  .what { grid-column: 1 / -1; }
  .from { grid-column: 1 / -1; justify-self: start; }
  .body { padding-left: var(--space-4); }
}
</style>
