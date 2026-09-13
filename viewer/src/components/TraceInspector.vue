<script setup lang="ts">
import { computed, ref, watch } from "vue";
import type { ShapeMismatch, TestTrace, TraceArtifact, TraceEntry, TraceTreeItem } from "../trace-schema";
import { categoryForEntry, entrySummary, formatDate, formatDuration, milliseconds, relativeTime, tone } from "../trace-utils";
import TraceValue from "./TraceValue.vue";
import TraceRelationNode from "./TraceRelationNode.vue";
import ShapeResultTree from "./ShapeResultTree.vue";
import ArtifactViewer from "./ArtifactViewer.vue";
import { useHorizontalResize } from "../use-horizontal-resize";

const props = defineProps<{
  test: TestTrace;
  entry?: TraceEntry | null;
  readArtifact?: (artifact: TraceArtifact) => Promise<Blob>;
}>();
const emit = defineEmits<{ close: []; select: [entry: TraceEntry] }>();
const panel = useHorizontalResize("prototrace.inspector-width", 480, 360, () => Math.min(900, window.innerWidth - 280), -1);
const tab = ref("overview");
watch(() => props.entry?.id, () => {
  if (props.entry?.kind.startsWith("attachment.")) tab.value = "artifact";
  else if (props.entry?.kind.includes("shape") || props.entry?.attributes?.["shape.result"]) tab.value = "shape";
  else if (props.entry?.error) tab.value = "error";
  else tab.value = "overview";
}, { immediate: true });

const parent = computed(() => props.test.entries.find(item => item.id === props.entry?.parentId));
const children = computed(() => props.test.entries.filter(item => item.parentId === props.entry?.id));
const relationTree = computed(() => {
  if (!props.entry) return [];
  const items = new Map<string, TraceTreeItem>(props.test.entries.map(entry => [entry.id, { entry, children: [] }]));
  const selected = items.get(props.entry.id);
  if (!selected) return [];
  for (const entry of props.test.entries) {
    const item = items.get(entry.id)!;
    if (entry.parentId && items.has(entry.parentId)) items.get(entry.parentId)!.children.push(item);
  }
  const chain: TraceTreeItem[] = [];
  let current: TraceTreeItem | undefined = selected;
  while (current) { chain.unshift(current); current = current.entry.parentId ? items.get(current.entry.parentId) : undefined; }
  for (let index = 0; index < chain.length - 1; index++) chain[index].children = [chain[index + 1]];
  return chain.length ? [chain[0]] : [];
});
function parseAttribute<T>(name: string, fallback: T): T {
  const value = props.entry?.attributes?.[name];
  if (!value) return fallback;
  try { return JSON.parse(value) as T; } catch { return fallback; }
}
const shapeMatches = computed(() => parseAttribute<string[]>("shape.matches", []));
const shapeMismatches = computed(() => parseAttribute<ShapeMismatch[]>("shape.mismatches", []));
const shapeExpected = computed(() => parseAttribute<unknown>("shape.expected", undefined));
const shapeActual = computed(() => parseAttribute<unknown>("shape.actual", undefined));
const hasShape = computed(() => Boolean(props.entry?.kind.includes("shape") || props.entry?.attributes?.["shape.result"]));
const artifact = computed(() => {
  if (!props.entry?.kind.startsWith("attachment.")) return undefined;
  const id = props.entry.attributes?.["attachment.artifact_id"];
  const name = props.entry.attributes?.["attachment.name"];
  return props.test.artifacts?.find(item => (id && item.id === id) || item.name === name);
});
const attributeGroups = computed(() => {
  const groups = new Map<string, Array<[string, string | null]>>();
  const labels: Record<string, string> = {
    http: "HTTP", graphql: "GraphQL", auth: "Authentication", client: "Client",
    context: "Context", expected: "Expected", actual: "Actual", matched: "Match result",
    observation: "Observation", attachment: "Artifact", server: "Server", request: "Request"
  };
  for (const pair of Object.entries(props.entry?.attributes ?? {})) {
    const label = labels[pair[0].split(".")[0]] ?? "Other";
    groups.set(label, [...(groups.get(label) ?? []), pair]);
  }
  return [...groups.entries()];
});
const tabs = computed(() => props.entry ? [
  { id: "overview", label: "Summary" },
  { id: "relations", label: `Relations · ${children.value.length}` },
  ...(hasShape.value ? [{ id: "shape", label: `Shape ${shapeMismatches.value.length ? `· ${shapeMismatches.value.length} failed` : `· ${shapeMatches.value.length} matched`}` }] : []),
  ...(artifact.value ? [{ id: "artifact", label: "Artifact" }] : []),
  { id: "data", label: `Raw data · ${Object.keys(props.entry.attributes ?? {}).length}` },
  ...(props.entry.error ? [{ id: "error", label: "Error" }] : [])
] : []);
</script>

<template>
  <div v-if="entry" class="drawer-backdrop" @click.self="emit('close')">
    <aside class="detail-panel" :style="{ width: `${panel.size.value}px` }" role="dialog" aria-modal="true" aria-label="Trace entry details">
      <div class="drawer-resizer" role="separator" aria-label="Resize inspector" aria-orientation="vertical" @pointerdown="panel.startResize" />
      <div class="panel-heading inspector-heading">
        <div><strong>Inspector</strong><span>{{ entry.kind }}</span></div>
        <button type="button" aria-label="Close inspector" @click="emit('close')">×</button>
      </div>
      <div class="detail-tabs" role="tablist">
        <button v-for="item in tabs" :key="item.id" type="button" role="tab" :class="{ active: tab === item.id }" @click="tab = item.id">{{ item.label }}</button>
      </div>
      <div class="detail-content">
        <template v-if="tab === 'overview'">
          <div class="inspector-kicker"><span class="category-chip">{{ categoryForEntry(entry) }}</span><span class="outcome-pill" :class="tone(entry.outcome)">{{ entry.outcome }}</span></div>
          <h2>{{ entry.name }}</h2>
          <p class="entry-narrative">{{ entrySummary(entry) }}</p>
          <div class="time-card"><span>Started {{ relativeTime(entry, test) }}</span><strong>{{ entry.duration ? formatDuration(milliseconds(entry.duration)) : 'Instant event' }}</strong><small>{{ formatDate(entry.timestampUtc) }}</small></div>
          <button type="button" class="relation-summary" @click="tab = 'relations'"><span>Trace position</span><strong>{{ parent ? `Under ${parent.name}` : 'Root operation' }}</strong><small>{{ children.length }} direct children · open relation tree →</small></button>
          <h3>Technical identity</h3>
          <dl class="facts"><dt>Phase</dt><dd>{{ entry.phase }}</dd><dt>Source</dt><dd>{{ entry.source }}</dd><dt>Type</dt><dd>{{ entry.entryKind }}</dd><dt>Entry ID</dt><dd>{{ entry.id }}</dd></dl>
        </template>
        <template v-else-if="tab === 'relations'">
          <div class="relation-tree-heading"><span class="drawing-label">Operation hierarchy</span><p>Ancestors lead to the selected entry; its complete subtree is shown underneath.</p></div>
          <div class="relation-tree"><TraceRelationNode v-for="item in relationTree" :key="item.entry.id" :item="item" :selected-id="entry.id" :depth="0" @select="emit('select', $event)" /></div>
        </template>
        <template v-else-if="tab === 'shape'">
          <div class="shape-summary" :class="shapeMismatches.length ? 'danger' : 'success'">
            <strong>{{ shapeMismatches.length ? `${shapeMismatches.length} mismatches` : 'Shape matched' }}</strong>
            <span>{{ shapeMatches.length }} properties matched successfully</span>
          </div>
          <ShapeResultTree :matches="shapeMatches" :mismatches="shapeMismatches" :expected="shapeExpected" :actual="shapeActual" />
          <details class="raw-shape-values">
            <summary>Raw expected and actual data</summary>
            <div class="shape-values">
              <section><h3>Expected JSON</h3><TraceValue :value="shapeExpected" bare /></section>
              <section><h3>Actual JSON</h3><TraceValue :value="shapeActual" bare /></section>
            </div>
          </details>
        </template>
        <template v-else-if="tab === 'data'">
          <div v-if="attributeGroups.length" class="attribute-groups">
            <section v-for="([label, attributes]) in attributeGroups" :key="label"><h3>{{ label }}</h3><dl class="attributes"><template v-for="([key, value]) in attributes" :key="key"><dt>{{ key }}</dt><dd><TraceValue :value="value" /></dd></template></dl></section>
          </div>
          <div v-else class="inspector-empty">This entry did not record additional attributes.</div>
        </template>
        <template v-else-if="tab === 'artifact' && artifact">
          <ArtifactViewer :artifact="artifact" :read-artifact="readArtifact" />
        </template>
        <div v-else-if="tab === 'error' && entry.error" class="error-card"><span class="drawing-label">Failure</span><strong>{{ entry.error.type }}</strong><p>{{ entry.error.message }}</p><pre v-if="entry.error.stackTrace">{{ entry.error.stackTrace }}</pre></div>
      </div>
    </aside>
  </div>
</template>
