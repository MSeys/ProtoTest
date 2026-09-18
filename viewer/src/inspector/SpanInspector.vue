<script setup lang="ts">
import { computed } from "vue";
import type { Artifact, Item, Span, TestTrace } from "../trace/model";
import { shapeMismatches } from "../trace/model";
import { formatBytes, formatDuration, formatOffset, isCheck, itemTitle, shortType, sourceLabels, tone } from "../trace/format";
import { shapeTreeOf } from "../trace/shapes";
import SectionView from "./SectionView.vue";
import ShapeResultTree from "./ShapeResultTree.vue";
import JsonView from "./JsonView.vue";

/*
 * Everything one operation recorded, in the order a reader asks for it: what went wrong, what was compared,
 * the checks on it, what it sent and got back, what it changed, what it left as evidence. The identity -
 * name, kind, outcome - sits in the inspector's head above this.
 */
const props = defineProps<{ span: Span; test: TestTrace }>();
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
 * Values that are JSON open as a tree; the rest are text.
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
const attributeCount = computed(() => Object.keys(props.span.attributes).length);

function isJson(value: string | null): boolean {
  const trimmed = value?.trim() ?? "";
  return trimmed.startsWith("{") || trimmed.startsWith("[");
}

function value(input: unknown): string {
  return input === undefined ? "missing" : JSON.stringify(input) ?? String(input);
}
</script>

<template>
  <div class="span-inspector">
    <!-- With a shape tree or table below, the exception's message repeats it; it stays one click away. -->
    <section v-if="span.error" class="error">
      <h3>{{ shortType(span.error.type) }}</h3>
      <details v-if="mismatches.length">
        <summary>Exception message</summary>
        <pre>{{ span.error.message }}</pre>
      </details>
      <pre v-else>{{ span.error.message }}</pre>
    </section>

    <section v-if="shapeTree" class="block">
      <h3>Expected against actual</h3>
      <ShapeResultTree :nodes="shapeTree" />
      <JsonView v-if="actual" :value="actual" label="The response it judged" :open-depth="1" :marks="markedPaths" />
    </section>

    <section v-else-if="mismatches.length" class="block">
      <h3>Expected against actual</h3>
      <table class="mismatches">
        <thead><tr><th>Property</th><th>Expected</th><th>Actual</th></tr></thead>
        <tbody>
          <tr v-for="mismatch in mismatches" :key="mismatch.path" :title="mismatch.reason">
            <td>{{ mismatch.path }}</td><td class="expected">{{ value(mismatch.expected) }}</td><td class="actual">{{ value(mismatch.actual) }}</td>
          </tr>
        </tbody>
      </table>
    </section>

    <section v-if="checks.length" class="block">
      <h3>Checks on this call</h3>
      <button v-for="check in checks" :key="check.id" type="button" class="link-row" :class="tone(check.status)" @click="emit('select', check)">
        <i class="status" :class="tone(check.status)" />
        <span>{{ check.name }}</span>
        <small>{{ check.sections.flatMap(section => section.items)[0]?.value ?? "" }}</small>
      </button>
    </section>

    <SectionView v-for="section in span.sections" :key="section.label" :section="section" :brief="Boolean(shapeTree)" />

    <section v-if="span.changes.length" class="block">
      <h3>What this changed</h3>
      <button v-for="(change, index) in span.changes" :key="index" type="button" class="link-row" @click="emit('item', change.item)">
        <span class="change">{{ change.change }}</span>
        <span>{{ change.item.kind }} {{ itemTitle(change.item) }}</span>
        <small :class="change.source">{{ sourceLabels[change.source] }}</small>
      </button>
    </section>

    <section v-else-if="span.item" class="block">
      <h3>Acted on</h3>
      <button type="button" class="link-row" @click="span.item && emit('item', span.item)">
        <span class="change">{{ span.item.kind }}</span>
        <span>{{ itemTitle(span.item) }}</span>
      </button>
    </section>

    <section v-if="span.evidence.length" class="block">
      <h3>Evidence</h3>
      <div v-for="(item, index) in span.evidence" :key="index" class="evidence">
        <template v-if="item.type === 'observation'">
          <p><strong>Observed</strong> {{ item.kind }} <span class="muted">on {{ item.target }}</span></p>
          <JsonView v-if="item.data" :value="item.data" :label="item.identifier ?? item.kind" :open-depth="0" />
        </template>
        <template v-else-if="item.type === 'attachment'">
          <p><strong>Attached</strong> {{ item.name }}</p>
          <button v-if="item.artifact" type="button" class="open" :disabled="Boolean(item.artifact.error)"
                  @click="item.artifact && emit('artifact', item.artifact)">
            {{ item.artifact.error ? item.artifact.error : `Open ${item.artifact.mediaType}, ${formatBytes(item.artifact.sizeBytes)}` }}
          </button>
        </template>
        <template v-else>
          <p><strong>{{ item.status }} finding</strong> {{ item.message }}</p>
        </template>
      </div>
    </section>

    <section v-if="span.moments.length" class="block">
      <h3>What happened during it</h3>
      <div v-for="(moment, index) in span.moments" :key="index" class="moment">
        <span class="offset">{{ formatOffset(moment.at - test.start) }}</span>
        <span>{{ moment.name }}</span>
        <small>{{ moment.kind }}</small>
      </div>
    </section>

    <section v-if="inside.length" class="block">
      <h3>Inside</h3>
      <button v-for="child in inside" :key="child.id" type="button" class="link-row" :class="tone(child.status)" @click="emit('select', child)">
        <i class="status" :class="tone(child.status)" />
        <span>{{ child.name }}</span>
        <small>{{ formatDuration(child.duration) }}</small>
      </button>
    </section>

    <details v-if="attributeCount" class="attributes" open>
      <summary>Attributes <small>{{ attributeCount }}</small></summary>
      <section v-for="[group, entries] in attributeGroups" :key="group" class="attribute-group">
        <h4>{{ group }}</h4>
        <dl>
          <template v-for="[key, attribute] in entries" :key="key">
            <dt>{{ key }}</dt>
            <dd>
              <JsonView v-if="isJson(attribute)" :value="attribute" :open-depth="0" />
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
.open { justify-self: start; height: var(--row-height); padding: 0 var(--space-3); border: 1px solid var(--border-strong); border-radius: var(--radius-control); background: var(--surface-2); font-size: var(--text-micro); }
.open:hover:not(:disabled) { border-color: var(--blueprint); }
.moment { display: grid; grid-template-columns: 64px minmax(0, 1fr) auto; gap: var(--space-2); font-size: var(--text-meta); }
.moment .offset, .moment small { color: var(--muted); font-size: var(--text-micro); }

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
