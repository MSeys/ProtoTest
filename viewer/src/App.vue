<script setup lang="ts">
import { computed, ref } from "vue";
import AppHeader from "./ui/AppHeader.vue";
import RunView from "./views/RunView.vue";
import TestStoryView from "./views/TestStoryView.vue";
import TestTreeView from "./views/TestTreeView.vue";
import Inspector from "./inspector/Inspector.vue";
import StyleGuideView from "./views/StyleGuideView.vue";
import AppButton from "./ui/AppButton.vue";
import BrandMark from "./ui/BrandMark.vue";
import Tabs from "./ui/Tabs.vue";
import TestRail from "./ui/TestRail.vue";
import FailureBanner from "./ui/FailureBanner.vue";
import ArtifactOverlay from "./ui/ArtifactOverlay.vue";
import ColumnResizer from "./ui/ColumnResizer.vue";
import { useColumnResize } from "./ui/useColumnResize";
import { readProtoTrace } from "./model/trace-reader";
import type { LoadedTrace } from "./model/trace-reader";
import type { TestTrace, TraceArtifact, TraceEntry, TraceRun } from "./model/trace-schema";
import { primaryFailure } from "./model/trace-rows";
import { defaultTestTab, href, navigate, replace, route } from "./router";

const fileInput = ref<HTMLInputElement>();
const run = ref<TraceRun>();
const archive = ref<LoadedTrace>();
const fileName = ref("");
const error = ref("");
const openArtifact = ref<TraceArtifact>();
const railOpen = ref(true);
const dragging = ref(false);
const railResize = useColumnResize("prototrace.rail-width", { min: 200, max: 460, edge: "leading" });
const inspectorResize = useColumnResize("prototrace.inspector-width", { min: 300, max: 640, edge: "trailing" });

const activeTab = computed(() => route.value.name === "test" ? route.value.tab : "run");
const styleGuide = computed(() => route.value.name === "styleguide");
const selectedTest = computed<TestTrace | undefined>(() => {
  const current = route.value;
  return current.name === "test" ? run.value?.tests.find(test => test.testId === current.testId) : undefined;
});
// The selection lives in the address, so a link to a failing operation survives a reload.
const selectedEntry = computed<TraceEntry | undefined>(() => {
  const current = route.value;
  if (current.name !== "test" || !current.entryId) return undefined;
  return selectedTest.value?.entries.find(entry => entry.id === current.entryId);
});
const failure = computed(() => {
  const test = selectedTest.value;
  if (!test || test.outcome === "Succeeded" || test.outcome === "Skipped") return undefined;
  return primaryFailure(test);
});
const tabs = computed(() => {
  const test = selectedTest.value;
  const items = [{ id: "run", label: "Run", href: href({ name: "run" }) }];
  if (test) {
    const entryId = route.value.name === "test" ? route.value.entryId : undefined;
    items.push(
      { id: "story", label: "Story", href: href({ name: "test", testId: test.testId, tab: "story", entryId }) },
      { id: "tree", label: "Tree", href: href({ name: "test", testId: test.testId, tab: "tree", entryId }) });
  }
  return items;
});

function openPicker() { fileInput.value?.click(); }
function showTest(test: TestTrace) {
  // Land on the failure when there is one: that is what the reader came for.
  navigate({ name: "test", testId: test.testId, tab: defaultTestTab, entryId: primaryFailure(test)?.id });
}
function selectEntry(entry: TraceEntry | undefined) {
  const current = route.value;
  if (current.name !== "test") return;
  replace({ ...current, entryId: entry?.id });
}
async function loadBuffer(buffer: ArrayBuffer, name: string) {
  try {
    error.value = "";
    const loaded = await readProtoTrace(buffer);
    archive.value = loaded;
    run.value = loaded.run;
    fileName.value = name;
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : "The trace could not be opened.";
  }
}
async function loadFile(file: File) {
  await loadBuffer(await file.arrayBuffer(), file.name);
}
async function loadDemo() {
  try {
    error.value = "";
    const response = await fetch(`${import.meta.env.BASE_URL}demos/prototest-demo.prototrace`);
    if (!response.ok) throw new Error(`The demo trace could not be loaded (${response.status}).`);
    await loadBuffer(await response.arrayBuffer(), "ProtoTest demo trace");
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : "The demo trace could not be opened.";
  }
}
function fileChanged(event: Event) {
  const file = (event.target as HTMLInputElement).files?.[0];
  if (file) void loadFile(file);
}
// ?demo=1 loads the bundled trace, so a link (or a capture) never depends on clicking through the UI.
if (new URLSearchParams(location.search).has("demo")) void loadDemo();
function dropped(event: DragEvent) {
  dragging.value = false;
  const file = event.dataTransfer?.files?.[0];
  if (file) void loadFile(file);
}
</script>

<template>
  <AppHeader @open="openPicker" />
  <input ref="fileInput" type="file" accept=".prototrace,application/zip" hidden @change="fileChanged">
  <main>
    <div v-if="styleGuide" class="view-host"><StyleGuideView /></div>
    <section v-else-if="!run" class="empty-state">
      <div class="drop-zone" :class="{ dragging }" role="button" tabindex="0" aria-label="Open a ProtoTrace file"
           @click="openPicker" @keydown.enter.prevent="openPicker" @keydown.space.prevent="openPicker"
           @dragover.prevent="dragging = true" @dragleave="dragging = false" @drop.prevent="dropped">
        <BrandMark :size="88" />
        <span class="eyebrow">Trace input / local</span>
        <h1>Open a ProtoTest execution</h1>
        <p>Drop a <code>.prototrace</code> bundle here, choose one, or explore the bundled SaaS demo.</p>
        <div class="empty-actions">
          <AppButton variant="primary" @click.stop="openPicker">Choose trace file</AppButton>
          <AppButton @click.stop="loadDemo">Open demo trace</AppButton>
        </div>
        <small v-if="error" class="error">{{ error }}</small>
        <small v-else>Your files are processed locally. Nothing is uploaded. <a href="#/styleguide">Style guide</a></small>
      </div>
    </section>

    <section v-else class="workspace" :class="{ railed: selectedTest && railOpen, docked: selectedTest && selectedEntry }"
             :style="{
               '--rail-width': railResize.width.value ? `${railResize.width.value}px` : undefined,
               '--inspector-width': inspectorResize.width.value ? `${inspectorResize.width.value}px` : undefined
             }">
      <nav class="view-bar" aria-label="Views">
        <AppButton v-if="selectedTest" variant="icon" :label="railOpen ? 'Hide the test list' : 'Show the test list'"
                   @click="railOpen = !railOpen">{{ railOpen ? "◨" : "◧" }}</AppButton>
        <Tabs :items="tabs" :active="activeTab" variant="underline" />
      </nav>

      <TestRail v-if="selectedTest && railOpen" :tests="run.tests" :selected="selectedTest" @select="showTest" />
      <ColumnResizer v-if="selectedTest && railOpen" class="rail-resizer" label="Resize the test list"
                     @start="railResize.start" @reset="railResize.reset" />

      <div class="stack">
        <FailureBanner v-if="selectedTest && failure" :entry="failure" :outcome="selectedTest.outcome"
                       @inspect="selectEntry(failure)" />
        <div class="view-host">
          <RunView v-if="!selectedTest" :run="run" :file-name="fileName" @select="showTest" @artifact="openArtifact = $event" />
          <TestTreeView v-else-if="activeTab === 'tree'" :test="selectedTest" :selected="selectedEntry" @select="selectEntry" />
          <TestStoryView v-else :test="selectedTest" :selected="selectedEntry" @select="selectEntry" />
        </div>
      </div>

      <ColumnResizer v-if="selectedTest && selectedEntry" class="inspector-resizer" label="Resize the inspector"
                     @start="inspectorResize.start" @reset="inspectorResize.reset" />
      <Inspector v-if="selectedTest && selectedEntry" :test="selectedTest" :entry="selectedEntry"
                 :read-artifact="archive?.readArtifact" @select="selectEntry" @close="selectEntry(undefined)" />
    </section>

    <ArtifactOverlay :artifact="openArtifact" :read-artifact="archive?.readArtifact" @close="openArtifact = undefined" />
  </main>
</template>

<style scoped>
main { flex: 1 1 auto; min-height: 0; display: grid; grid-template-rows: minmax(0, 1fr); container-type: inline-size; }
.workspace {
  min-width: 0;
  min-height: 0;
  padding: var(--space-3) clamp(10px, 1.4vw, 20px) var(--space-4);
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  grid-template-rows: auto minmax(0, 1fr);
  gap: var(--space-3);
}
.view-bar {
  min-width: 0;
  display: flex;
  align-items: center;
  gap: var(--space-3);
  border-bottom: 1px solid var(--border);
}
.view-bar > nav { flex: 0 0 auto; }
.view-bar > button { margin-bottom: var(--space-1); }
.stack { min-width: 0; min-height: 0; display: grid; grid-template-rows: auto minmax(0, 1fr); gap: var(--space-3); }
.view-host { min-width: 0; min-height: 0; overflow: auto; scrollbar-gutter: stable; }
.empty-actions { display: flex; flex-wrap: wrap; gap: var(--space-3); justify-content: center; }
.drop-zone code { padding: 1px var(--space-2); border-radius: var(--radius-chip); background: var(--surface-2); font-family: var(--font-mono); font-size: var(--text-meta); }
.drop-zone .error { color: var(--danger); }

/* The rail and the inspector become columns when there is room, and step aside when there is not.
   Their widths are draggable, so the defaults are only a starting point. */
.workspace { --rail-default: clamp(220px, 19vw, 300px); --inspector-default: clamp(340px, 26vw, 460px); }
.rail-resizer, .inspector-resizer { display: none; }
.workspace > .inspector {
  position: fixed;
  z-index: 40;
  inset: auto 0 0 0;
  max-height: min(62dvh, 560px);
  border-bottom: 0;
  border-radius: var(--radius-overlay) var(--radius-overlay) 0 0;
  box-shadow: var(--elevation-overlay);
}
@container (max-width: 999px) {
  .workspace.railed > .rail { display: none; }
}
@container (min-width: 1000px) {
  .workspace.railed { grid-template-columns: var(--rail-width, var(--rail-default)) 7px minmax(0, 1fr); }
  .workspace.railed > .view-bar { grid-column: 1 / -1; }
  .rail-resizer { display: grid; }
}
@container (min-width: 1180px) {
  .workspace.docked { grid-template-columns: minmax(0, 1fr) 7px var(--inspector-width, var(--inspector-default)); }
  .workspace.railed.docked {
    grid-template-columns: var(--rail-width, var(--rail-default)) 7px minmax(0, 1fr) 7px var(--inspector-width, var(--inspector-default));
  }
  .workspace.docked > .view-bar { grid-column: 1 / -1; }
  .inspector-resizer { display: grid; }
  .workspace > .inspector { position: static; max-height: none; border-bottom: 1px solid var(--border); border-radius: var(--radius-panel); box-shadow: none; }
}
</style>
