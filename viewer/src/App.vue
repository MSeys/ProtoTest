<script setup lang="ts">
import { computed, nextTick, ref, watch } from "vue";
import AppHeader from "./ui/AppHeader.vue";
import RunView from "./views/RunView.vue";
import StoryView from "./views/StoryView.vue";
import StateView from "./views/StateView.vue";
import SpansView from "./views/SpansView.vue";
import Inspector from "./inspector/Inspector.vue";
import AppButton from "./ui/AppButton.vue";
import BrandMark from "./ui/BrandMark.vue";
import Tabs from "./ui/Tabs.vue";
import TestRail from "./ui/TestRail.vue";
import FailureCard from "./ui/FailureCard.vue";
import OutcomePill from "./ui/OutcomePill.vue";
import ArtifactOverlay from "./ui/ArtifactOverlay.vue";
import FileList from "./ui/FileList.vue";
import type { FileEntry } from "./ui/FileList.vue";
import Panel from "./ui/Panel.vue";
import ColumnResizer from "./ui/ColumnResizer.vue";
import Icon from "./ui/Icon.vue";
import { useColumnResize } from "./ui/useColumnResize";
import { openTraceArchive, TraceOpenError } from "./trace/archive";
import type { TraceArchive, TraceProblem } from "./trace/archive";
import { buildRun } from "./trace/model";
import type { Artifact, Item, Run, Span, TestTrace } from "./trace/model";
import { formatDuration, pad, testCodeName, testGroup, testTitle } from "./trace/format";
import { href, navigate, replace, route } from "./router";
import { useSources } from "./trace/sources";
import type { TestView } from "./router";

const fileInput = ref<HTMLInputElement>();
const run = ref<Run>();
const archive = ref<TraceArchive>();
const fileName = ref("");
const problem = ref<{ kind: TraceProblem | "load"; message: string }>();
const loading = ref(false);
const openArtifact = ref<Artifact>();
// Open as a column where it fits; as a drawer on a narrow screen it starts closed.
const railOpen = ref(matchMedia("(min-width: 1000px)").matches);
const dragging = ref(false);
const railResize = useColumnResize("prototrace.rail-width", { min: 200, max: 460, edge: "leading" });
const inspectorResize = useColumnResize("prototrace.inspector-width", { min: 300, max: 640, edge: "trailing" });

/*
 * On narrow screens the inspector is a bottom sheet. It has a definite height, so its body always scrolls,
 * and the grabber resizes it between a half sheet and nearly the whole screen. A px height is stored;
 * `null` means the default half sheet.
 */
const sheetHeight = ref<number | null>(null);
let sheetPointerId: number | null = null;
let sheetStartY = 0;
let sheetStartHeight = 0;
let sheetMoved = false;

function sheetDefault() { return Math.min(window.innerHeight * 0.62, 560); }
function sheetMaximum() { return window.innerHeight * 0.94; }
function sheetMinimum() { return window.innerHeight * 0.35; }
function sheetCurrent() { return sheetHeight.value ?? sheetDefault(); }

function startSheetDrag(event: PointerEvent) {
  sheetPointerId = event.pointerId;
  sheetStartY = event.clientY;
  sheetStartHeight = sheetCurrent();
  sheetMoved = false;
  (event.currentTarget as HTMLElement).setPointerCapture?.(event.pointerId);
  window.addEventListener("pointermove", moveSheetDrag);
  window.addEventListener("pointerup", endSheetDrag, { once: true });
  window.addEventListener("pointercancel", endSheetDrag, { once: true });
  event.preventDefault();
}
function moveSheetDrag(event: PointerEvent) {
  if (sheetPointerId === null) return;
  const delta = sheetStartY - event.clientY;
  if (Math.abs(delta) > 4) sheetMoved = true;
  sheetHeight.value = Math.min(Math.max(sheetStartHeight + delta, sheetMinimum()), sheetMaximum());
}
function endSheetDrag() {
  window.removeEventListener("pointermove", moveSheetDrag);
  window.removeEventListener("pointercancel", endSheetDrag);
  if (sheetPointerId === null) return;
  sheetPointerId = null;
  const half = sheetDefault();
  const full = sheetMaximum();
  const current = sheetCurrent();
  // A tap toggles; a drag snaps to whichever of the two it ended nearer.
  const toFull = !sheetMoved ? current <= (half + full) / 2 : current > (half + full) / 2;
  sheetHeight.value = toFull ? full : null;
}
function nudgeSheet(delta: number) {
  sheetHeight.value = Math.min(Math.max(sheetCurrent() + delta, sheetMinimum()), sheetMaximum());
}

/*
 * Two widths change behaviour, not just layout: from 1000px the test list is a column (below, a drawer), and
 * from 1180px the inspector docks beside the view (below, a sheet over it). They follow main's width, which
 * spans the window, so the window's media queries stand in for main's container queries here.
 */
function widthQuery(minimum: number) {
  const query = matchMedia(`(min-width: ${minimum}px)`);
  const matches = ref(query.matches);
  query.addEventListener("change", event => { matches.value = event.matches; });
  return matches;
}
const railFits = widthQuery(1000);
const inspectorDocks = widthQuery(1180);
watch(railFits, fits => { railOpen.value = fits; });
// Escape closes the topmost thing: an open artifact handles its own, then the drawer, then the details.
addEventListener("keydown", event => {
  if (event.key !== "Escape" || openArtifact.value) return;
  if (!railFits.value && railOpen.value) { railOpen.value = false; return; }
  if (inspecting.value) selectSpan(undefined);
});

/*
 * ↑ and ↓ walk the rows the reader can see - in the order the view shows them, folded rows skipped - and the
 * details follow. Typing in a field keeps its arrows.
 */
addEventListener("keydown", event => {
  if (event.key !== "ArrowDown" && event.key !== "ArrowUp") return;
  if (event.altKey || event.ctrlKey || event.metaKey || openArtifact.value || !selectedTest.value) return;
  const target = event.target as HTMLElement | null;
  if (target?.closest("input, textarea, select, [contenteditable]")) return;
  const ids = [...new Set([...(viewHost.value?.querySelectorAll<HTMLElement>("[data-span]") ?? [])].map(element => element.dataset.span!))];
  if (!ids.length) return;
  const current = selectedSpan.value ? ids.indexOf(selectedSpan.value.id) : -1;
  const next = current < 0 ? (event.key === "ArrowDown" ? 0 : ids.length - 1)
    : Math.min(ids.length - 1, Math.max(0, current + (event.key === "ArrowDown" ? 1 : -1)));
  const span = selectedTest.value.byId.get(ids[next]);
  if (!span) return;
  event.preventDefault();
  selectSpan(span);
});

const selectedTest = computed<TestTrace | undefined>(() => {
  const current = route.value;
  return current.name === "test" ? run.value?.tests.find(test => test.id === current.testId) : undefined;
});
// A link to a test this trace does not hold - an old link, a different run - says so instead of pretending.
const missingTestId = computed(() => {
  const current = route.value;
  return current.name === "test" && run.value && !selectedTest.value ? current.testId : undefined;
});
const view = computed<TestView | "run">(() => route.value.name === "test" && !missingTestId.value ? route.value.view : "run");

// The selection lives in the address, so a link to a failing check survives a reload and a share.
const selectedSpan = computed<Span | undefined>(() => {
  const current = route.value;
  if (current.name !== "test" || !current.selection || !("span" in current.selection)) return undefined;
  return selectedTest.value?.byId.get(current.selection.span);
});
const selectedItem = computed<Item | undefined>(() => {
  const current = route.value;
  if (current.name !== "test" || !current.selection || !("item" in current.selection)) return undefined;
  const { kind, id } = current.selection.item;
  return [...(selectedTest.value?.items ?? []), ...(run.value?.items ?? [])].find(item => item.kind === kind && item.id === id);
});
// A new test or view starts at its top; only the selection within one view keeps the reader's place.
const viewHost = ref<HTMLElement>();
watch(() => [route.value.name === "test" ? route.value.testId : "", view.value], () => {
  void nextTick(() => viewHost.value?.scrollTo({ top: 0 }));
});
const itemSelection = computed(() => selectedItem.value ? { kind: selectedItem.value.kind, id: selectedItem.value.id } : undefined);
const inspecting = computed(() => Boolean(selectedTest.value && (selectedSpan.value || selectedItem.value)));

/**
 * The test a test tab opens when none is chosen yet: the first that failed, then the first that went partial
 * or was cancelled, then the first that ran. That is the test a reader would look for first.
 */
const defaultTest = computed<TestTrace | undefined>(() => {
  const tests = run.value?.tests ?? [];
  return tests.find(test => test.outcome === "failed")
    ?? tests.find(test => test.outcome === "partial" || test.outcome === "cancelled")
    ?? tests[0];
});

/**
 * Where opening a test lands. Beside a docked inspector the failing check is selected, because that is what
 * the reader came for. As a sheet it would cover a small screen with what the failure card already says.
 */
function landing(test: TestTrace): { span: string } | undefined {
  const failing = test.failure?.span;
  return inspectorDocks.value && failing ? { span: failing.id } : undefined;
}

// Every tab is always there, in the same place: the test tabs open the chosen test, or the default one.
const testFiles = computed<FileEntry[]>(() => [...(selectedTest.value?.artifacts.values() ?? [])]
  .map(artifact => ({ artifact, detail: artifact.description })));

const tabs = computed(() => {
  const test = selectedTest.value ?? defaultTest.value;
  const items = [{ id: "run", label: "Run", href: href({ name: "run" }) }];
  if (!test) return items;
  const current = route.value;
  const selection = selectedTest.value && current.name === "test" ? current.selection : landing(test);
  const views: [TestView, string][] = [["story", "Story"], ["state", "State"], ["spans", "Spans"], ["files", "Files"]];
  for (const [id, label] of views) items.push({ id, label, href: href({ name: "test", testId: test.id, view: id, selection }) });
  return items;
});

function openPicker() { fileInput.value?.click(); }
function showTest(test: TestTrace) {
  const current = route.value;
  // Switching test keeps the view the reader chose; only a first visit lands on the story.
  const next = current.name === "test" ? current.view : "story";
  navigate({ name: "test", testId: test.id, view: next, selection: landing(test) });
  // On a narrow screen the test list is a drawer: picking a test is the reason it was opened.
  if (!railFits.value) railOpen.value = false;
}
function selectSpan(span: Span | undefined) {
  const current = route.value;
  if (current.name !== "test") return;
  replace({ ...current, selection: span ? { span: span.id } : undefined });
}
function selectItem(item: Item) {
  const current = route.value;
  if (current.name !== "test") return;
  replace({ ...current, selection: { item: { kind: item.kind, id: item.id } } });
}
function readArtifact(artifact: Artifact) {
  if (!archive.value) return Promise.reject(new Error("No trace is open."));
  return archive.value.readFile(artifact.archivePath, artifact.mediaType);
}

async function loadBuffer(buffer: ArrayBuffer, name: string) {
  loading.value = true;
  problem.value = undefined;
  try {
    const opened = await openTraceArchive(buffer);
    run.value = buildRun(opened.spans, opened.state);
    archive.value = opened;
    useSources(path => opened.readSource(path));
    fileName.value = name;
  } catch (reason) {
    problem.value = reason instanceof TraceOpenError
      ? { kind: reason.problem, message: reason.message }
      : { kind: "corrupt", message: reason instanceof Error ? reason.message : "The trace could not be opened." };
  } finally {
    loading.value = false;
  }
}
async function loadFile(file: File) {
  await loadBuffer(await file.arrayBuffer(), file.name);
}
async function loadDemo() {
  loading.value = true;
  try {
    const response = await fetch(`${import.meta.env.BASE_URL}demos/prototest-demo.prototrace`);
    if (!response.ok) throw new Error(`The demo trace could not be loaded (${response.status}).`);
    await loadBuffer(await response.arrayBuffer(), "ProtoTest demo trace");
  } catch (reason) {
    problem.value = { kind: "load", message: reason instanceof Error ? reason.message : "The demo trace could not be opened." };
    loading.value = false;
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

const problemTitle = computed(() => ({
  legacy: "This trace is from an older ProtoTest",
  corrupt: "This file is not a readable trace",
  unsupported: "This trace cannot be read here",
  load: "The trace could not be loaded"
})[problem.value?.kind ?? "corrupt"]);
</script>

<template>
  <AppHeader @open="openPicker" />
  <input ref="fileInput" type="file" accept=".prototrace,application/zip" hidden @change="fileChanged">
  <main>
    <section v-if="!run" class="empty-state">
      <div class="drop-zone" :class="{ dragging }" role="button" tabindex="0" aria-label="Open a ProtoTrace file"
           @click="openPicker" @keydown.enter.prevent="openPicker" @keydown.space.prevent="openPicker"
           @dragover.prevent="dragging = true" @dragleave="dragging = false" @drop.prevent="dropped">
        <BrandMark :size="88" />
        <template v-if="loading">
          <h1>Reading the trace</h1>
          <p>Large runs take a moment; everything stays on this machine.</p>
        </template>
        <template v-else-if="problem">
          <h1>{{ problemTitle }}</h1>
          <p v-if="problem.kind !== 'legacy'" class="problem">{{ problem.message }}</p>
          <p v-if="problem.kind === 'legacy'">Run the tests again with the current ProtoTest to produce a trace this viewer reads.</p>
          <div class="empty-actions">
            <AppButton variant="primary" @click.stop="openPicker">Choose another file</AppButton>
            <AppButton @click.stop="loadDemo">Open the demo trace</AppButton>
          </div>
        </template>
        <template v-else>
          <h1>Open a ProtoTest execution</h1>
          <p>Drop a <code>.prototrace</code> file here, choose one, or explore the bundled SaaS demo.</p>
          <div class="empty-actions">
            <AppButton variant="primary" @click.stop="openPicker">Choose trace file</AppButton>
            <AppButton @click.stop="loadDemo">Open demo trace</AppButton>
          </div>
          <small>Your files are read in this browser. Nothing is uploaded.</small>
        </template>
      </div>
    </section>

    <section v-else class="workspace" :class="{ railed: selectedTest && railOpen, docked: inspecting }"
             :style="{
               '--rail-width': railResize.width.value ? `${railResize.width.value}px` : undefined,
               '--inspector-width': inspectorResize.width.value ? `${inspectorResize.width.value}px` : undefined,
               '--inspector-sheet-height': sheetHeight === null ? undefined : `${sheetHeight}px`
             }">
      <nav class="view-bar" aria-label="Views">
        <!-- Always rendered so the tabs never shift; on the run screen the run itself is the list. -->
        <AppButton variant="icon" class="rail-toggle" :class="{ inert: !selectedTest }" :disabled="!selectedTest"
                   :label="railOpen ? 'Hide the test list' : 'Show the test list'" @click="railOpen = !railOpen">
          <Icon name="sidebar" />
        </AppButton>
        <Tabs :items="tabs" :active="view" variant="underline" />
      </nav>

      <button v-if="selectedTest && railOpen && !railFits" class="rail-scrim" type="button" aria-label="Close the test list"
              @click="railOpen = false" />
      <TestRail v-if="selectedTest && railOpen" :tests="run.tests" :selected="selectedTest" @select="showTest" />
      <ColumnResizer v-if="selectedTest && railOpen" class="rail-resizer" label="Resize the test list"
                     @start="railResize.start" @reset="railResize.reset" />

      <div ref="viewHost" class="view-host">
        <div v-if="missingTestId" class="missing-test">
          <h2>This trace has no such test</h2>
          <p>The link names <code>{{ missingTestId }}</code>, which is not in {{ fileName }}. It may come from another run.</p>
          <AppButton variant="primary" @click="navigate({ name: 'run' })">Back to the run</AppButton>
        </div>
        <RunView v-else-if="!selectedTest" :run="run" :file-name="fileName" @select="showTest" @artifact="openArtifact = $event" />
        <div v-else class="test">
          <header class="test-head">
            <b>{{ pad(selectedTest.number) }}</b>
            <div class="test-title">
              <h1>{{ testTitle(selectedTest) }}</h1>
              <p><span>{{ testGroup(selectedTest) }}</span><code>{{ testCodeName(selectedTest) }}</code></p>
            </div>
            <OutcomePill :outcome="selectedTest.outcome" :detail="formatDuration(selectedTest.duration)" />
          </header>
          <FailureCard v-if="selectedTest.failure && selectedTest.outcome !== 'succeeded'" :failure="selectedTest.failure"
                       :outcome="selectedTest.outcome" @select="selectSpan" />
          <StoryView v-if="view === 'story'" :test="selectedTest" :selected="selectedSpan?.id" @select="selectSpan" />
          <StateView v-else-if="view === 'state'" :test="selectedTest" :selected="itemSelection" :selected-span="selectedSpan?.id"
                     @select-item="selectItem" @select-span="selectSpan" />
          <Panel v-else-if="view === 'files'" title="Files" subtitle="Everything this test attached, in the order it produced them." pad="none">
            <FileList :entries="testFiles" @open="openArtifact = $event" />
          </Panel>
          <SpansView v-else :test="selectedTest" :selected="selectedSpan?.id" @select="selectSpan" />
        </div>
      </div>

      <ColumnResizer v-if="inspecting" class="inspector-resizer" label="Resize the details"
                     @start="inspectorResize.start" @reset="inspectorResize.reset" />
      <Inspector v-if="selectedTest && inspecting" :test="selectedTest" :span="selectedSpan" :item="selectedItem"
                 @select="selectSpan" @item="selectItem" @artifact="openArtifact = $event" @close="selectSpan(undefined)" />
      <button v-if="inspecting" class="inspector-handle" type="button"
              aria-label="Resize the details" title="Drag to resize, tap to expand"
              @pointerdown="startSheetDrag" @keydown.up.prevent="nudgeSheet(64)" @keydown.down.prevent="nudgeSheet(-64)" />
    </section>

    <ArtifactOverlay :artifact="openArtifact" :read-artifact="readArtifact" @close="openArtifact = undefined" />
  </main>
</template>

<style scoped>
main { flex: 1 1 auto; min-height: 0; display: grid; grid-template-rows: minmax(0, 1fr); container-type: inline-size; }
.workspace {
  min-width: 0;
  min-height: 0;
  /* No bottom padding: the view scrolls to the window's edge, and keeps its breathing room inside the scroll. */
  padding: var(--space-3) clamp(10px, 1.4vw, 20px) 0;
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
/* Held in place on the run screen so the tabs never move, but there is nothing for it to open there. */
.rail-toggle.inert { visibility: hidden; }
/* The one scroller for a view. Its content runs to the bottom edge of the window and ends with its own
   margin, so a scrolled view is cut by the window - not by a strip of background above it. */
.view-host { min-width: 0; min-height: 0; padding-bottom: var(--space-4); overflow: auto; overscroll-behavior: contain; scrollbar-gutter: stable; }
/* The rail and the docked inspector scroll inside themselves, so they keep the gap the view scrolls into. */
.workspace > .rail { margin-bottom: var(--space-4); }

.test { display: grid; gap: var(--space-3); }
.test-head { padding: var(--space-1) var(--space-1) 0; display: grid; grid-template-columns: auto minmax(0, 1fr) auto; align-items: start; gap: var(--space-3); }
.test-head > b { padding-top: var(--space-1); color: var(--dim); font: var(--text-meta) var(--font-mono); }
.test-title { min-width: 0; display: grid; gap: 2px; }
.test-title h1 { font-size: var(--text-heading); letter-spacing: var(--tracking-display); line-height: var(--leading-tight); overflow-wrap: anywhere; }
.test-title p { display: flex; flex-wrap: wrap; gap: var(--space-1) var(--space-3); color: var(--muted); font-size: var(--text-micro); }
.test-title code { overflow-wrap: anywhere; color: var(--dim); font-family: var(--font-mono); }
.test-head :deep(.pill) { padding-top: var(--space-1); }

.empty-actions { display: flex; flex-wrap: wrap; gap: var(--space-3); justify-content: center; }
.drop-zone h1 { margin-top: var(--space-3); }
.drop-zone code { padding: 1px var(--space-2); border-radius: var(--radius-chip); background: var(--surface-2); font-family: var(--font-mono); font-size: var(--text-meta); }
.drop-zone .problem { color: var(--danger); }

/* The rail and the inspector become columns when there is room, and step aside when there is not.
   Their widths are draggable, so the defaults are only a starting point. */
.workspace { --rail-default: clamp(220px, 19vw, 300px); --inspector-default: clamp(340px, 28vw, 480px); }
.rail-resizer, .inspector-resizer { display: none; }
/* A definite height, not a max: the inspector's body is then always the thing that scrolls. */
.workspace > .inspector {
  position: fixed;
  z-index: 40;
  inset: auto 0 0 0;
  height: var(--inspector-sheet-height, min(62dvh, 560px));
  max-height: 94dvh;
  border-bottom: 0;
  border-radius: var(--radius-overlay) var(--radius-overlay) 0 0;
  box-shadow: var(--elevation-overlay);
}
/* The grabber is the sheet's one control: it sits on the sheet's top edge and moves it. */
.inspector-handle {
  position: fixed;
  z-index: 41;
  left: 50%;
  bottom: var(--inspector-sheet-height, min(62dvh, 560px));
  width: 72px;
  height: 20px;
  padding: 0;
  border: 0;
  transform: translateX(-50%);
  background: transparent;
  cursor: grab;
  touch-action: none;
}
.inspector-handle::before {
  content: "";
  position: absolute;
  left: 50%;
  top: 50%;
  width: 36px;
  height: 4px;
  border-radius: var(--radius-pill);
  transform: translate(-50%, -50%);
  background: var(--border-strong);
}
.inspector-handle:hover::before { background: var(--muted); }
.inspector-handle:active { cursor: grabbing; }
/* Below the column width the test list is a drawer over the view; picking a test or the scrim closes it. */
@container (max-width: 999px) {
  .workspace.railed > .rail {
    position: fixed;
    z-index: 51;
    inset: 0 auto 0 0;
    width: min(86vw, 340px);
    margin: 0;
    border-block: 0;
    border-left: 0;
    border-radius: 0 var(--radius-overlay) var(--radius-overlay) 0;
    box-shadow: var(--elevation-overlay);
  }
}
/* Under a sheet, the view keeps the sheet's height free at its end and scrolls a selection above it, so
   nothing the reader picked hides behind the details it opened. */
@container (max-width: 1179px) {
  .workspace.docked > .view-host {
    padding-bottom: calc(var(--inspector-sheet-height, min(62dvh, 560px)) + var(--space-4));
    scroll-padding-bottom: calc(var(--inspector-sheet-height, min(62dvh, 560px)) + var(--space-4));
  }
}
.rail-scrim { position: fixed; z-index: 50; inset: 0; padding: 0; border: 0; background: color-mix(in srgb, var(--pt-navy-abyss) 48%, transparent); cursor: default; }
.missing-test { max-width: 560px; margin: var(--space-6) auto; padding: var(--space-5); display: grid; gap: var(--space-3); justify-items: start; border: 1px dashed var(--border-strong); border-radius: var(--radius-panel); }
.missing-test h2 { margin: 0; font-size: var(--text-title); }
.missing-test p { margin: 0; color: var(--muted); overflow-wrap: anywhere; }
.missing-test code { font-family: var(--font-mono); font-size: var(--text-meta); }
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
  .inspector-handle { display: none; }
  .workspace > .inspector { position: static; height: auto; max-height: none; margin-bottom: var(--space-4); border-bottom: 1px solid var(--border); border-radius: var(--radius-panel); box-shadow: none; }
}
</style>
