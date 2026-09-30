<script setup lang="ts">
import { computed, ref } from "vue";
import type { Artifact, Item, Run, Span, TestTrace } from "../trace/model";
import { shapeMismatches } from "../trace/model";
import { formatBytes, formatDuration, formatOffset, firstCheckValue, isCheck, itemKindLabel, itemTitle, jsonLiteral, shortType, sourceLabels, tone } from "../trace/format";
import { shapeTreeOf } from "../trace/shapes";
import SectionView from "./SectionView.vue";
import ShapeResultTree from "./ShapeResultTree.vue";
import JsonView from "./JsonView.vue";
import SourceView from "./SourceView.vue";
import { isJsonLike } from "./json";
import { sourceLocation } from "../trace/sources";

/*
 * Everything one operation recorded, in the order a reader asks for it: what went wrong, what was compared,
 * the checks on it, what it sent and got back, what it changed, what it left as evidence. The identity -
 * name, kind, outcome - sits in the inspector's head above this.
 */
const props = defineProps<{ span: Span; test: TestTrace | Run }>();
const emit = defineEmits<{ select: [span: Span]; item: [item: Item]; artifact: [artifact: Artifact] }>();

const checks = computed(() => props.span.children.filter(isCheck));
const inside = computed(() => props.span.children.filter(child => !isCheck(child)));
const mismatches = computed(() => shapeMismatches(props.span));
// A ProtoTest.Json shape check records the whole comparison; it reads best as the document it validated.
const shapeTree = computed(() => shapeTreeOf(props.span));
/** The response a shape check judged, with the mismatched properties marked in it. */
const actual = computed(() => props.span.attributes["shape.actual"] ?? null);
const markedPaths = computed(() => mismatches.value.map(mismatch => mismatch.path));

/**
 * Every attribute, grouped by its namespace (http, auth, shape, ...) so a long list reads as a few short ones.
 * Values that are JSON open as a tree; the rest are text. A long operation starts folded: the dedicated views
 * above (source, comparison, request and response) already say what matters, and the raw list stays one click away.
 */
const attributeGroups = computed(() => {
  const groups = new Map<string, [string, string | null][]>();
  for (const [key, value] of Object.entries(props.span.attributes).sort(([left], [right]) => left.localeCompare(right))) {
    const dot = key.indexOf(".");
    const group = dot > 0 ? key.slice(0, dot) : "other";
    groups.set(group, [...(groups.get(group) ?? []), [dot > 0 ? key.slice(dot + 1) : key, value]]);
  }
  return [...groups.entries()];
});
// Where in the suite's code this operation started, when the trace recorded it.
const location = computed(() => sourceLocation(props.span.attributes));
const attributeCount = computed(() => Object.keys(props.span.attributes).length);
/** A short list reads inline; past a few groups or a handful of values it folds, one click away. */
const attributesOpen = computed(() => attributeGroups.value.length <= 3 && attributeCount.value <= 8);

type Finding = Extract<Span["evidence"][number], { type: "finding" }>;
/** A finding's own facts: how it is filed, where it points, and what it carried. */
function findingFacts(item: Finding): [string, string][] {
  const facts: [string, string][] = [];
  if (item.category) facts.push(["category", item.category]);
  if (item.tags.length) facts.push(["tags", item.tags.join(", ")]);
  if (item.target) facts.push(["target", item.target]);
  for (const [key, value] of Object.entries(item.metadata)) if (value !== null) facts.push([key, value]);
  return facts;
}

/*
 * The index: one link per block this operation has, so a long detail - an error, the source, a comparison,
 * request and response, evidence, moments - is reached in one step, not by scrolling past the rest.
 */
const index = computed(() => {
  const entries: { id: string; label: string; hot?: boolean }[] = [];
  if (props.span.error) entries.push({ id: "error", label: "Error", hot: true });
  if (location.value) entries.push({ id: "source", label: "Source" });
  if (shapeTree.value || mismatches.value.length) entries.push({ id: "comparison", label: "Comparison", hot: props.span.status === "failed" });
  if (checks.value.length) entries.push({ id: "checks", label: "Checks" });
  props.span.sections.forEach((section, number) => entries.push({ id: `section-${number}`, label: section.label }));
  if (props.span.changes.length || props.span.item) entries.push({ id: "changes", label: "Changed" });
  if (props.span.evidence.length) entries.push({ id: "evidence", label: "Evidence" });
  if (props.span.moments.length) entries.push({ id: "moments", label: "Moments" });
  if (inside.value.length) entries.push({ id: "inside", label: "Inside" });
  if (attributeCount.value) entries.push({ id: "attributes", label: "Attributes" });
  return entries;
});
const root = ref<HTMLElement>();
function jump(id: string) {
  const target = root.value?.querySelector<HTMLElement>(`[data-block="${id}"]`);
  if (target instanceof HTMLDetailsElement) target.open = true;
  target?.scrollIntoView({ block: "start", behavior: "smooth" });
}
</script>

<template>
  <div ref="root" class="span-inspector">
    <nav v-if="index.length > 2" class="index" aria-label="Parts of this operation">
      <button v-for="entry in index" :key="entry.id" type="button" :class="{ hot: entry.hot }" @click="jump(entry.id)">{{ entry.label }}</button>
    </nav>
    <!-- With a shape tree or table below, the exception's message repeats it; it stays one click away. -->
    <section v-if="span.error" class="error" data-block="error">
      <h3>{{ shortType(span.error.type) }}</h3>
      <details v-if="mismatches.length">
        <summary>Exception message</summary>
        <pre>{{ span.error.message }}</pre>
      </details>
      <pre v-else>{{ span.error.message }}</pre>
    </section>

    <SourceView v-if="location" :location="location" data-block="source" />

    <!-- The tree carries its own head (the validated document and its legend), so no second title above it. -->
    <section v-if="shapeTree" class="block" data-block="comparison">
      <ShapeResultTree :nodes="shapeTree" />
      <JsonView v-if="actual" :value="actual" label="The response it judged" :open-depth="1" :marks="markedPaths" />
    </section>

    <section v-else-if="mismatches.length" class="block" data-block="comparison">
      <h3>Expected against actual</h3>
      <table class="mismatches">
        <thead><tr><th>Property</th><th>Expected</th><th>Actual</th></tr></thead>
        <tbody>
          <tr v-for="mismatch in mismatches" :key="mismatch.path" :title="mismatch.reason">
            <td>{{ mismatch.path }}</td><td class="expected">{{ mismatch.expected === undefined ? "missing" : jsonLiteral(mismatch.expected) }}</td><td class="actual">{{ mismatch.actual === undefined ? "missing" : jsonLiteral(mismatch.actual) }}</td>
          </tr>
        </tbody>
      </table>
    </section>

    <section v-if="checks.length" class="block" data-block="checks">
      <h3>Checks on this call</h3>
      <button v-for="check in checks" :key="check.id" type="button" class="link-row" :class="tone(check.status)" @click="emit('select', check)">
        <i class="status" :class="tone(check.status)" />
        <span>{{ check.name }}</span>
        <small>{{ firstCheckValue(check) }}</small>
      </button>
    </section>

    <SectionView v-for="(section, number) in span.sections" :key="section.label" :section="section" :brief="Boolean(shapeTree)" :data-block="`section-${number}`" />

    <section v-if="span.changes.length" class="block" data-block="changes">
      <h3>What this changed</h3>
      <button v-for="(change, index) in span.changes" :key="index" type="button" class="link-row" @click="emit('item', change.item)">
        <span class="change">{{ change.change }}</span>
        <span>{{ itemKindLabel(change.item).label }} {{ itemTitle(change.item) }}</span>
        <small :class="change.source">{{ sourceLabels[change.source] }}</small>
      </button>
    </section>

    <section v-else-if="span.item" class="block" data-block="changes">
      <h3>Acted on</h3>
      <button type="button" class="link-row" @click="span.item && emit('item', span.item)">
        <span class="change">{{ span.item ? itemKindLabel(span.item).label : "" }}</span>
        <span>{{ itemTitle(span.item) }}</span>
      </button>
    </section>

    <section v-if="span.evidence.length" class="block" data-block="evidence">
      <h3>Evidence</h3>
      <div v-for="(item, index) in span.evidence" :key="index" class="evidence">
        <template v-if="item.type === 'observation'">
          <p><strong>Observed</strong> {{ item.kind }} <span class="muted">on {{ item.target }}</span></p>
          <JsonView v-if="item.data" :value="item.data" :label="item.identifier ?? item.kind" :open-depth="0" />
          <dl v-if="Object.keys(item.metadata).length" class="facts-list">
            <template v-for="(value, key) in item.metadata" :key="key"><dt>{{ key }}</dt><dd>{{ value ?? "null" }}</dd></template>
          </dl>
        </template>
        <template v-else-if="item.type === 'attachment'">
          <button v-if="item.artifact" type="button" class="link-row file" :disabled="Boolean(item.artifact.error)"
                  :title="item.artifact.error ?? `Open ${item.name}`" @click="item.artifact && emit('artifact', item.artifact)">
            <span class="change">File</span>
            <span>{{ item.name }}</span>
            <small>{{ item.artifact.error ? "Unavailable" : `${item.artifact.mediaType}, ${formatBytes(item.artifact.sizeBytes)}` }}</small>
          </button>
          <p v-else><strong>Attached</strong> {{ item.name }} <span class="muted">with no file</span></p>
        </template>
        <template v-else>
          <p><strong>{{ item.status }} finding</strong> {{ item.message }}</p>
          <dl v-if="findingFacts(item).length" class="facts-list">
            <template v-for="[key, value] in findingFacts(item)" :key="key"><dt>{{ key }}</dt><dd>{{ value }}</dd></template>
          </dl>
        </template>
      </div>
    </section>

    <section v-if="span.moments.length" class="block" data-block="moments">
      <h3>What happened during it</h3>
      <div v-for="(moment, number) in span.moments" :key="number" class="moment" :class="tone(moment.outcome)">
        <span class="offset">{{ formatOffset(moment.at - test.start) }}</span>
        <span class="name">{{ moment.name }}</span>
        <small>{{ moment.kind }}</small>
        <div v-if="moment.error || Object.keys(moment.attributes).length || moment.sections.length" class="moment-detail">
          <pre v-if="moment.error" class="moment-error">{{ moment.error.message }}</pre>
          <dl v-if="Object.keys(moment.attributes).length" class="facts-list">
            <template v-for="(value, key) in moment.attributes" :key="key"><dt>{{ key }}</dt><dd>{{ value ?? "null" }}</dd></template>
          </dl>
          <SectionView v-for="section in moment.sections" :key="section.label" :section="section" />
        </div>
      </div>
    </section>

    <section v-if="inside.length" class="block" data-block="inside">
      <h3>Inside</h3>
      <button v-for="child in inside" :key="child.id" type="button" class="link-row" :class="tone(child.status)" @click="emit('select', child)">
        <i class="status" :class="tone(child.status)" />
        <span>{{ child.name }}</span>
        <small>{{ formatDuration(child.duration) }}</small>
      </button>
    </section>

    <details v-if="attributeCount" class="attributes" :open="attributesOpen" data-block="attributes">
      <summary>Attributes <small>{{ attributeCount }}</small></summary>
      <section v-for="[group, entries] in attributeGroups" :key="group" class="attribute-group">
        <h4>{{ group }}</h4>
        <dl>
          <template v-for="[key, attribute] in entries" :key="key">
            <dt>{{ key }}</dt>
            <dd>
              <JsonView v-if="isJsonLike(attribute)" :value="attribute" :open-depth="0" />
              <span v-else-if="attribute === null" class="null">null</span>
              <template v-else>{{ attribute }}</template>
            </dd>
          </template>
        </dl>
      </section>
    </details>
  </div>
</template>

<style scoped>
.span-inspector { min-width: 0; display: grid; gap: var(--space-5); }

h3 { font-family: var(--font-ui); font-size: var(--text-meta); font-weight: var(--weight-bold); letter-spacing: 0; }
.block { min-width: 0; display: grid; gap: var(--space-2); }

.error { padding: var(--space-3); display: grid; gap: var(--space-2); border-left: 2px solid var(--danger); border-radius: 0 var(--radius-chip) var(--radius-chip) 0; background: var(--danger-soft); }
.error h3 { color: var(--danger); }
.error summary { color: var(--muted); font-size: var(--text-micro); cursor: pointer; }
.error details pre { margin-top: var(--space-2); }
.error pre { margin: 0; white-space: pre-wrap; overflow-wrap: anywhere; font: var(--text-micro)/var(--leading) var(--font-mono); }

.mismatches { width: 100%; border-collapse: collapse; font-size: var(--text-meta); }
.mismatches th { padding-bottom: var(--space-1); color: var(--muted); font-size: var(--text-micro); text-align: left; }
.mismatches td { padding: var(--space-1) var(--space-3) var(--space-1) 0; border-top: 1px solid var(--border); overflow-wrap: anywhere; font-family: var(--font-mono); vertical-align: top; }
.mismatches .expected { color: var(--muted); }
.mismatches .actual { color: var(--danger); font-weight: var(--weight-bold); }

.link-row {
  width: 100%;
  min-height: var(--row-height);
  padding: 0 var(--space-2);
  display: grid;
  grid-template-columns: auto minmax(0, 1fr) auto;
  align-items: center;
  gap: var(--space-2);
  border: 1px solid transparent;
  border-radius: var(--radius-chip);
  background: var(--surface-2);
  text-align: left;
  font-size: var(--text-meta);
  transition: border-color var(--motion-fast) var(--motion-ease);
}
.link-row:hover { border-color: var(--blueprint); }
.link-row span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.link-row small { color: var(--muted); font-size: var(--text-micro); }
.link-row small.applicationside { color: var(--blueprint); font-weight: var(--weight-semibold); }
.change { color: var(--muted); font-size: var(--text-micro); font-weight: var(--weight-semibold); }

.evidence { min-width: 0; display: grid; gap: var(--space-2); padding-top: var(--space-2); border-top: 1px solid var(--border); }
.evidence p { font-size: var(--text-meta); overflow-wrap: anywhere; }
.link-row:disabled { cursor: not-allowed; opacity: .6; }
.link-row:disabled:hover { border-color: transparent; }
.moment { display: grid; grid-template-columns: 64px minmax(0, 1fr) auto; gap: var(--space-2); font-size: var(--text-meta); }
.moment .offset, .moment small { color: var(--muted); font-size: var(--text-micro); }
/* A moment that went wrong reads as such; an informational one stays quiet. */
.moment.danger .name { color: var(--danger); font-weight: var(--weight-semibold); }
.moment.warning .name { color: var(--warning); }

/* The index sits at the top of the details and stays there while they scroll. */
.index {
  position: sticky;
  top: calc(var(--space-4) * -1);
  z-index: 1;
  margin: calc(var(--space-4) * -1) calc(var(--space-4) * -1) 0;
  padding: var(--space-2) var(--space-4);
  display: flex;
  gap: var(--space-1);
  overflow-x: auto;
  scrollbar-width: none;
  mask-image: linear-gradient(to right, black calc(100% - var(--space-6)), transparent);
  border-bottom: 1px solid var(--border);
  background: var(--surface);
}
.index button { flex: none; height: 22px; padding: 0 var(--space-2); border: 1px solid var(--border); border-radius: var(--radius-pill); background: transparent; color: var(--muted); font-size: var(--text-meta); }
.index button:hover { border-color: var(--blueprint); color: var(--text); }
.index button.hot { border-color: var(--danger); color: var(--danger); }
.facts-list { margin: 0; display: grid; grid-template-columns: minmax(80px, max-content) minmax(0, 1fr); gap: 0 var(--space-3); }
.facts-list dt, .facts-list dd { min-width: 0; margin: 0; padding: 2px 0; border-top: 1px solid var(--border); overflow-wrap: anywhere; font: var(--text-meta)/var(--leading) var(--font-mono); }
.facts-list dt { color: var(--muted); }
.moment-detail { grid-column: 2 / -1; display: grid; gap: var(--space-2); padding-bottom: var(--space-2); }
.moment-error { margin: 0; white-space: pre-wrap; overflow-wrap: anywhere; color: var(--danger); font: var(--text-meta)/var(--leading) var(--font-mono); }
.attributes { padding-top: var(--space-3); border-top: 1px solid var(--border); }
.attributes > summary { display: flex; align-items: center; gap: var(--space-2); font-size: var(--text-meta); font-weight: var(--weight-bold); cursor: pointer; }
.attributes > summary small { color: var(--muted); font-weight: var(--weight-regular); }
.attribute-group { margin-top: var(--space-3); }
.attribute-group h4 { margin: 0 0 var(--space-1); color: var(--muted); font-family: var(--font-ui); font-size: var(--text-micro); font-weight: var(--weight-semibold); }
.attribute-group dl { margin: 0; display: grid; grid-template-columns: minmax(88px, 34%) minmax(0, 1fr); gap: 0 var(--space-3); }
.attribute-group dt, .attribute-group dd { min-width: 0; padding: var(--space-1) 0; border-top: 1px solid var(--border); font-size: var(--text-micro); overflow-wrap: anywhere; }
.attribute-group dt { color: var(--muted); font-family: var(--font-mono); }
.attribute-group dd { margin: 0; font-family: var(--font-mono); }
.attribute-group .null { color: var(--dim); }
</style>
