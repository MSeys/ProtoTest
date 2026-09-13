<script setup lang="ts">
import { ref } from "vue";
import AppHeader from "./components/AppHeader.vue";
import RunOverview from "./components/RunOverview.vue";
import TestNavigator from "./components/TestNavigator.vue";
import TestOverview from "./components/TestOverview.vue";
import TraceEmptyState from "./components/TraceEmptyState.vue";
import TraceExplorer from "./components/TraceExplorer.vue";
import TraceInspector from "./components/TraceInspector.vue";
import LifecycleTimeline from "./components/LifecycleTimeline.vue";
import ArtifactDialog from "./components/ArtifactDialog.vue";
import { readProtoTrace } from "./trace-reader";
import type { LoadedTrace } from "./trace-reader";
import type { TestTrace, TraceArtifact, TraceEntry, TraceRun } from "./trace-schema";
import { useHorizontalResize } from "./use-horizontal-resize";

const fileInput = ref<HTMLInputElement>();
const run = ref<TraceRun>();
const archive = ref<LoadedTrace>();
const fileName = ref("");
const error = ref("");
const selectedTest = ref<TestTrace>();
const selectedEntry = ref<TraceEntry>();
const selectedRunArtifact = ref<TraceArtifact>();
const workspaceView = ref("overview");
const testColumn = useHorizontalResize("prototrace.test-column-width", 260, 210, () => Math.min(460, window.innerWidth * .4));
const workspaceViews = [
  { id: "overview", label: "Test story" },
  { id: "lifecycle", label: "Lifecycle" },
  { id: "explorer", label: "Trace explorer" }
];

function openPicker() { fileInput.value?.click(); }
function selectTest(test: TestTrace) { selectedTest.value = test; selectedEntry.value = undefined; workspaceView.value = "overview"; }
async function loadBuffer(buffer: ArrayBuffer, name: string) {
  try {
    error.value = "";
    const loaded = await readProtoTrace(buffer);
    archive.value = loaded;
    run.value = loaded.run;
    fileName.value = name;
    selectedTest.value = loaded.run.tests[0];
    selectedEntry.value = undefined;
    workspaceView.value = "overview";
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
    const response = await fetch(`${import.meta.env.BASE_URL}demos/shape-mismatch.prototrace`);
    if (!response.ok) throw new Error(`The demo trace could not be loaded (${response.status}).`);
    await loadBuffer(await response.arrayBuffer(), "Built-in shape mismatch demo");
  } catch (reason) {
    error.value = reason instanceof Error ? reason.message : "The demo trace could not be opened.";
  }
}
function fileChanged(event: Event) {
  const file = (event.target as HTMLInputElement).files?.[0];
  if (file) void loadFile(file);
}
</script>

<template>
  <AppHeader @open="openPicker" />
  <input ref="fileInput" type="file" accept=".prototrace,application/zip" hidden @change="fileChanged">
  <main>
    <TraceEmptyState v-if="!run" :error="error" @open="openPicker" @demo="loadDemo" @file="loadFile" />
    <section v-else class="workspace">
      <RunOverview :run="run" :file-name="fileName" @select="selectTest" @artifact="selectedRunArtifact = $event" />
      <div v-if="selectedTest" class="viewer-grid" :style="{ '--test-panel-width': `${testColumn.size.value}px` }">
        <TestNavigator :tests="run.tests" :selected="selectedTest" @select="selectTest" />
        <div class="column-resizer" role="separator" aria-label="Resize test column" aria-orientation="vertical" @pointerdown="testColumn.startResize" />
        <div class="test-workspace">
          <nav class="workspace-tabs" aria-label="Test views"><button v-for="item in workspaceViews" :key="item.id" type="button" :class="{ active: workspaceView === item.id }" @click="workspaceView = item.id">{{ item.label }}</button></nav>
          <TestOverview v-if="workspaceView === 'overview'" :test="selectedTest" @select="selectedEntry = $event" />
          <LifecycleTimeline v-else-if="workspaceView === 'lifecycle'" :test="selectedTest" @select="selectedEntry = $event" />
          <TraceExplorer v-else :key="selectedTest.testId" :test="selectedTest" :selected="selectedEntry" @select="selectedEntry = $event" />
        </div>
        <TraceInspector :test="selectedTest" :entry="selectedEntry" :read-artifact="archive?.readArtifact" @select="selectedEntry = $event" @close="selectedEntry = undefined" />
      </div>
      <ArtifactDialog :artifact="selectedRunArtifact" :read-artifact="archive?.readArtifact" @close="selectedRunArtifact = undefined" />
    </section>
  </main>
</template>
