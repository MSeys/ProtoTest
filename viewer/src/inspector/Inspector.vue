<script setup lang="ts">
import { computed, ref, watch } from "vue";
import type { ShapeMismatch, TestTrace, TraceArtifact, TraceEntry } from "../model/trace-schema";
import { entrySummary, entryTitle, formatDate, formatDuration, milliseconds, relativeTime, tone } from "../model/trace-format";
import { nodeType } from "../model/trace-levels";
import KindChip from "../ui/KindChip.vue";
import OutcomePill from "../ui/OutcomePill.vue";
import AppButton from "../ui/AppButton.vue";
import Tabs from "../ui/Tabs.vue";
import EmptyState from "../ui/EmptyState.vue";
import ArtifactView from "../ui/ArtifactView.vue";
import TraceValue from "./TraceValue.vue";
import ShapeResultTree from "./ShapeResultTree.vue";

const props = defineProps<{
  test: TestTrace;
  entry: TraceEntry;
  readArtifact?: (artifact: TraceArtifact) => Promise<Blob>;
}>();
const emit = defineEmits<{ close: []; select: [entry: TraceEntry] }>();
const tab = ref("summary");

const byId = computed(() => new Map(props.test.entries.map(entry => [entry.id, entry])));
const parent = computed(() => props.entry.parentId ? byId.value.get(props.entry.parentId) : undefined);
const children = computed(() => props.test.entries.filter(item => item.parentId === props.entry.id));
/** Root first, the selected entry last: the path the execution took to get here. */
const ancestors = computed(() => {
  const chain: TraceEntry[] = [];
  let current = parent.value;
  while (current) {
    chain.unshift(current);
    current = current.parentId ? byId.value.get(current.parentId) : undefined;
  }
  return chain;
});

function parseAttribute<T>(name: string, fallback: T): T {
  const value = props.entry.attributes?.[name];
  if (!value) return fallback;
  try { return JSON.parse(value) as T; } catch { return fallback; }
}
const shapeMatches = computed(() => parseAttribute<string[]>("shape.matches", []));
const shapeMismatches = computed(() => parseAttribute<ShapeMismatch[]>("shape.mismatches", []));
const shapeExpected = computed(() => parseAttribute<unknown>("shape.expected", undefined));
const shapeActual = computed(() => parseAttribute<unknown>("shape.actual", undefined));
const hasShape = computed(() => Boolean(props.entry.kind.includes("shape") || props.entry.attributes?.["shape.result"]));
const artifact = computed(() => {
  if (!props.entry.kind.startsWith("attachment.")) return undefined;
  const id = props.entry.attributes?.["attachment.artifact_id"];
  const name = props.entry.attributes?.["attachment.name"];
  return props.test.artifacts?.find(item => (id && item.id === id) || item.name === name);
});

const groupLabels: Record<string, string> = {
  http: "HTTP", graphql: "GraphQL", rest: "REST", auth: "Authentication", client: "Client",
  context: "Context", expected: "Expected", actual: "Actual", matched: "Match result",
  observation: "Observation", attachment: "Artifact", server: "Server", request: "Request",
  resource: "Resource", data: "Data", finding: "Finding", gate: "Gate", shape: "Shape",
  hook: "Hook", attribute: "Attribute", test: "Test", webhook: "Webhook"
};
const attributeGroups = computed(() => {
  const groups = new Map<string, [string, string | null][]>();
  for (const pair of Object.entries(props.entry.attributes ?? {})) {
    const prefix = pair[0].split(".")[0];
    const label = groupLabels[prefix] ?? `${prefix.charAt(0).toLocaleUpperCase()}${prefix.slice(1)}`;
    groups.set(label, [...(groups.get(label) ?? []), pair]);
  }
  return [...groups.entries()];
});
const attributeCount = computed(() => Object.keys(props.entry.attributes ?? {}).length);

const tabs = computed(() => [
  { id: "summary", label: "Summary" },
  ...(hasShape.value ? [{ id: "shape", label: shapeMismatches.value.length ? `Shape · ${shapeMismatches.value.length} failed` : "Shape" }] : []),
  ...(artifact.value ? [{ id: "artifact", label: "Artifact" }] : []),
  { id: "relations", label: `Relations · ${children.value.length}` },
  { id: "data", label: `Raw · ${attributeCount.value}` },
  ...(props.entry.error ? [{ id: "error", label: "Error" }] : [])
]);

// Open on what the reader came for: the artifact, the shape diff, the failure — then the summary.
watch(() => props.entry.id, () => {
  tab.value = props.entry.kind.startsWith("attachment.") ? "artifact"
    : hasShape.value && shapeMismatches.value.length ? "shape"
      : props.entry.error ? "error"
        : "summary";
}, { immediate: true });
</script>

<template>
  <aside class="inspector" role="complementary" aria-label="Entry details">
    <header class="head">
      <div class="identity">
        <div class="kicker">
          <KindChip :type="nodeType(entry)" />
          <OutcomePill :outcome="entry.outcome" />
          <span class="spacer" />
          <AppButton variant="icon" label="Close inspector" @click="emit('close')">×</AppButton>
        </div>
        <h2>{{ entryTitle(entry) }}</h2>
        <p class="kind mono">{{ entry.kind }}</p>
      </div>
      <Tabs :items="tabs" :active="tab" variant="underline" @select="tab = $event" />
    </header>

    <div class="body">
      <template v-if="tab === 'summary'">
        <p class="narrative">{{ entrySummary(entry) }}</p>

        <div class="timing">
          <div><span>Started</span><strong class="mono">{{ relativeTime(entry, test) }}</strong></div>
          <div><span>Took</span><strong class="mono">{{ entry.duration ? formatDuration(milliseconds(entry.duration)) : "instant" }}</strong></div>
          <div><span>Phase</span><strong>{{ entry.phase }}</strong></div>
        </div>

        <section v-if="ancestors.length" class="block">
          <h3>Carried by</h3>
          <ol class="path">
            <li v-for="(item, index) in ancestors" :key="item.id" :style="{ '--depth': index }">
              <button type="button" @click="emit('select', item)">
                <KindChip :type="nodeType(item)" />
                <span>{{ entryTitle(item) }}</span>
              </button>
            </li>
            <li class="here" :style="{ '--depth': ancestors.length }">
              <span class="current"><i class="status" :class="tone(entry.outcome)" />{{ entryTitle(entry) }}</span>
            </li>
          </ol>
        </section>

        <section class="block">
          <h3>Identity</h3>
          <dl class="facts">
            <dt>Source</dt><dd class="mono">{{ entry.source }}</dd>
            <dt>Type</dt><dd class="mono">{{ entry.entryKind }}</dd>
            <dt>Recorded</dt><dd class="mono">{{ formatDate(entry.timestampUtc) }}</dd>
            <dt>Entry id</dt><dd class="mono">{{ entry.id }}</dd>
          </dl>
        </section>
      </template>

      <template v-else-if="tab === 'relations'">
        <section v-if="ancestors.length" class="block">
          <h3>Path to here</h3>
          <ol class="path">
            <li v-for="(item, index) in ancestors" :key="item.id" :style="{ '--depth': index }">
              <button type="button" @click="emit('select', item)">
                <KindChip :type="nodeType(item)" />
                <span>{{ entryTitle(item) }}</span>
                <small class="mono">{{ item.duration ? formatDuration(milliseconds(item.duration)) : "" }}</small>
              </button>
            </li>
            <li class="here" :style="{ '--depth': ancestors.length }">
              <span class="current"><i class="status" :class="tone(entry.outcome)" />{{ entryTitle(entry) }}</span>
            </li>
          </ol>
        </section>

        <section class="block">
          <h3>Carried {{ children.length }}</h3>
          <div v-if="children.length" class="children">
            <button v-for="child in children" :key="child.id" type="button" class="child"
                    :title="child.name" @click="emit('select', child)">
              <KindChip :type="nodeType(child)" />
              <span>{{ entryTitle(child) }}</span>
              <small class="mono">{{ child.duration ? formatDuration(milliseconds(child.duration)) : "" }}</small>
              <i class="status" :class="tone(child.outcome)" />
            </button>
          </div>
          <EmptyState v-else message="This entry carried nothing; it is a leaf of the trace." />
        </section>
      </template>

      <template v-else-if="tab === 'shape'">
        <div class="verdict" :class="shapeMismatches.length ? 'danger' : 'success'">
          <strong>{{ shapeMismatches.length ? `${shapeMismatches.length} mismatches` : "Shape matched" }}</strong>
          <span>{{ shapeMatches.length }} properties matched</span>
        </div>
        <ShapeResultTree :matches="shapeMatches" :mismatches="shapeMismatches" :expected="shapeExpected" :actual="shapeActual" />
        <details class="raw-shape">
          <summary>Expected and actual, verbatim</summary>
          <div class="shape-values">
            <section><h3>Expected</h3><TraceValue :value="shapeExpected" bare /></section>
            <section><h3>Actual</h3><TraceValue :value="shapeActual" bare /></section>
          </div>
        </details>
      </template>

      <template v-else-if="tab === 'data'">
        <section v-for="[label, attributes] in attributeGroups" :key="label" class="block">
          <h3>{{ label }}</h3>
          <dl class="facts">
            <template v-for="[key, value] in attributes" :key="key">
              <dt :title="key">{{ key }}</dt>
              <dd><TraceValue :value="value" /></dd>
            </template>
          </dl>
        </section>
        <EmptyState v-if="!attributeGroups.length" message="This entry recorded no attributes." />
      </template>

      <ArtifactView v-else-if="tab === 'artifact' && artifact" :artifact="artifact" :read-artifact="readArtifact" />

      <div v-else-if="tab === 'error' && entry.error" class="failure">
        <strong class="mono">{{ entry.error.type }}</strong>
        <p>{{ entry.error.message }}</p>
        <pre v-if="entry.error.stackTrace"><code>{{ entry.error.stackTrace }}</code></pre>
      </div>
    </div>
  </aside>
</template>

<style scoped>
.inspector {
  min-width: 0;
  min-height: 0;
  display: grid;
  grid-template-rows: auto minmax(0, 1fr);
  border: 1px solid var(--border);
  border-radius: var(--radius-panel);
  background: var(--surface);
  overflow: hidden;
}
.head { padding: var(--space-3) var(--space-4) 0; display: grid; gap: var(--space-3); border-bottom: 1px solid var(--border); }
.identity { min-width: 0; display: grid; gap: var(--space-1); }
.kicker { display: flex; align-items: center; gap: var(--space-2); }
.kicker .spacer { flex: 1 1 auto; }
.identity h2 { font-size: var(--text-title); letter-spacing: -.015em; overflow-wrap: anywhere; }
.kind { color: var(--muted); font-size: var(--text-micro); overflow-wrap: anywhere; }

.body { min-height: 0; padding: var(--space-4); display: grid; gap: var(--space-5); align-content: start; overflow: auto; overflow-wrap: anywhere; }
.narrative { color: var(--muted); font-size: var(--text-meta); }

/* Three facts a reader always wants, before any of the detail. */
.timing { display: grid; grid-template-columns: repeat(auto-fit, minmax(84px, 1fr)); gap: var(--space-2); }
.timing > div {
  padding: var(--space-2) var(--space-3);
  display: grid;
  gap: 1px;
  border: 1px solid var(--border);
  border-left: 2px solid var(--blueprint);
  border-radius: 0 var(--radius-control) var(--radius-control) 0;
  background: var(--surface-2);
}
.timing span { color: var(--muted); font-size: var(--text-micro); }
.timing strong { font-size: var(--text-meta); }

.block { display: grid; gap: var(--space-2); }
.block h3 { color: var(--dim); font-family: var(--font-ui); font-size: var(--text-micro); font-weight: var(--weight-semibold); letter-spacing: .1em; text-transform: uppercase; }

/* The path is the same drawn trace the tree uses, so the two views teach the same thing. */
.path { margin: 0; padding: 0; display: grid; gap: 1px; list-style: none; }
.path li { position: relative; padding-left: calc(var(--depth) * 12px); }
.path li::before {
  content: "";
  position: absolute;
  left: calc(var(--depth) * 12px - 7px);
  top: 0;
  bottom: 50%;
  border-left: 1px solid var(--border);
  border-bottom: 1px solid var(--border);
  width: 6px;
}
.path li:first-child::before { display: none; }
.path button, .path .current {
  width: 100%;
  min-height: var(--row-height);
  padding: 0 var(--space-2);
  display: grid;
  grid-template-columns: auto minmax(0, 1fr) auto;
  align-items: center;
  gap: var(--space-2);
  border: 1px solid transparent;
  border-radius: var(--radius-chip);
  background: transparent;
  text-align: left;
}
.path button:hover { background: var(--hover); }
.path span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: var(--text-meta); }
.path small { color: var(--muted); font-size: var(--text-micro); }
.path .current { grid-template-columns: 7px minmax(0, 1fr); border-color: var(--blueprint); background: var(--blueprint-soft); font-size: var(--text-meta); }

.children { display: grid; gap: 1px; }
.child {
  min-height: var(--row-height);
  padding: 0 var(--space-2);
  display: grid;
  grid-template-columns: auto minmax(0, 1fr) auto 7px;
  align-items: center;
  gap: var(--space-2);
  border: 1px solid transparent;
  border-radius: var(--radius-chip);
  background: transparent;
  text-align: left;
}
.child:hover { background: var(--hover); }
.child span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: var(--text-meta); }
.child small { color: var(--muted); font-size: var(--text-micro); }

/* Key/value pairs: the label reads, the value is data and stays mono. */
.facts { margin: 0; display: grid; grid-template-columns: minmax(96px, 34%) minmax(0, 1fr); gap: 0 var(--space-4); }
.facts dt, .facts dd { min-width: 0; padding: var(--space-2) 0; border-top: 1px solid var(--border); }
.facts dt { color: var(--muted); font-size: var(--text-micro); }
.facts dd { margin: 0; font-size: var(--text-meta); }

.verdict {
  padding: var(--space-2) var(--space-3);
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: var(--space-2) var(--space-3);
  border-left: 2px solid var(--border);
  border-radius: 0 var(--radius-control) var(--radius-control) 0;
}
.verdict.success { border-color: var(--success); background: var(--success-soft); }
.verdict.danger { border-color: var(--danger); background: var(--danger-soft); }
.verdict strong { font-size: var(--text-strong); }
.verdict span { color: var(--muted); font-size: var(--text-micro); }
.raw-shape { border: 1px solid var(--border); border-radius: var(--radius-control); overflow: hidden; }
.raw-shape > summary { padding: var(--space-2) var(--space-3); background: var(--surface-2); color: var(--muted); font-size: var(--text-micro); }
.shape-values { padding: var(--space-3); display: grid; gap: var(--space-3); }
.shape-values h3 { margin-bottom: var(--space-1); color: var(--dim); font-size: var(--text-micro); letter-spacing: .08em; text-transform: uppercase; }

.failure {
  padding: var(--space-4);
  display: grid;
  gap: var(--space-2);
  border: 1px solid var(--danger-line);
  border-radius: var(--radius-control);
  background: var(--danger-soft);
}
.failure strong { color: var(--danger); font-size: var(--text-meta); }
.failure p { font-size: var(--text-meta); }
.failure pre {
  max-height: 340px;
  margin: 0;
  padding: var(--space-3);
  overflow: auto;
  border-radius: var(--radius-chip);
  background: var(--surface-sunken);
  color: var(--text-on-sunken);
  font: var(--text-micro)/1.6 var(--font-mono);
  white-space: pre-wrap;
}
</style>
