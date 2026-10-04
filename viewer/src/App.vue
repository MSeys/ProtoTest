<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from "vue";
import AppHeader, { type Crumb } from "./ui/AppHeader.vue";
import DemoList from "./ui/DemoList.vue";
import RunView from "./views/RunView.vue";
import TestView from "./views/TestView.vue";
import Inspector from "./inspector/Inspector.vue";
import AppButton from "./ui/AppButton.vue";
import BrandMark from "./ui/BrandMark.vue";
import TestRail from "./ui/TestRail.vue";
import ArtifactOverlay from "./ui/ArtifactOverlay.vue";
import ColumnResizer from "./ui/ColumnResizer.vue";
import Icon from "./ui/Icon.vue";
import { useColumnResize } from "./ui/useColumnResize";
import { orderedTests, resetTestFilter } from "./ui/testFilter";
import { openTraceArchive, TraceOpenError } from "./trace/archive";
import type { TraceArchive, TraceProblem } from "./trace/archive";
import { buildRun } from "./trace/model";
import type { Artifact, Item, Run, Span, TestTrace } from "./trace/model";
import { itemTitle, pad, testTitle } from "./trace/format";
import { href, navigate, replace, route } from "./router";
import { demos, demoFileUrl, resolveDemo, summarizeDemo, type DemoFacts } from "./demos";
import { parseTraceParam, shareUrl, traceNameFromUrl, type TraceSource } from "./share";
import { useSources } from "./trace/sources";
import type { RunView as RunViewId, TestView as TestViewId } from "./router";

const fileInput = ref<HTMLInputElement>();
const run = ref<Run>();
const archive = ref<TraceArchive>();
const fileName = ref("");
const problem = ref<{ kind: TraceProblem | "load"; message: string }>();
const loading = ref(false);
const openArtifact = ref<Artifact>();
const source = ref<TraceSource>();
const demoFacts = ref<Record<string, DemoFacts | null>>({});
// The demos sit in the start panel's sidebar; the reader can put them away and get the drop screen alone.
const demosOpen = ref(true);
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
 * Three widths change behaviour, not just layout: from 900px the test list is a column (below, a drawer);
 * from 1024px the details dock beside the view (below, a sheet over it); and from 1360px the list keeps
 * its width beside docked details (below, it steps aside to its numbers, so the view keeps room for time).
 */
function widthQuery(minimum: number) {
  const query = matchMedia(`(min-width: ${minimum}px)`);
  const matches = ref(query.matches);
  query.addEventListener("change", event => { matches.value = event.matches; });
  return matches;
}
const railFits = widthQuery(900);
const inspectorDocks = widthQuery(1024);
const railRoomy = widthQuery(1360);
// Open as a column where it fits; as a drawer on a narrow screen it starts closed.
const railOpen = ref(railFits.value);
/** The reader asked for the whole list while details are open. */
const railExpanded = ref(false);
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
  const ids = [...new Set([...(viewHost.value?.querySelectorAll<HTMLElement>("#test-view [data-span]") ?? [])].map(element => element.dataset.span!))];
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
const view = computed<TestViewId>(() => route.value.name === "test" ? route.value.view : "steps");

// The selection lives in the address, so a link to a failing check survives a reload and a share.
const selectedSpan = computed<Span | undefined>(() => {
  const current = route.value;
  if (!current.selection || !("span" in current.selection)) return undefined;
  const id = current.selection.span;
  return current.name === "test" ? selectedTest.value?.byId.get(id) : run.value?.spans.find(span => span.id === id);
});
const selectedItem = computed<Item | undefined>(() => {
  const current = route.value;
  if (!current.selection || !("item" in current.selection)) return undefined;
  const { kind, id } = current.selection.item;
  return [...(selectedTest.value?.items ?? []), ...(run.value?.items ?? [])].find(item => item.kind === kind && item.id === id);
});
// A new test or view starts at its top; only the selection within one view keeps the reader's place.
const viewHost = ref<HTMLElement>();
watch(() => [route.value.name === "test" ? route.value.testId : "", view.value], () => {
  void nextTick(() => viewHost.value?.scrollTo({ top: 0 }));
});
const inspecting = computed(() => Boolean(!missingTestId.value && (selectedSpan.value || selectedItem.value)));

/*
 * The list is a column where it fits and a drawer where it does not. Beside docked details on a screen
 * without room for both, it steps aside to its numbers until the reader asks for the whole list again.
 */
const railColumn = computed(() => Boolean(run.value) && railOpen.value && railFits.value);
const railDrawer = computed(() => Boolean(run.value) && railOpen.value && !railFits.value);
const railSlim = computed(() => railColumn.value && inspecting.value && inspectorDocks.value && !railRoomy.value && !railExpanded.value);
watch(inspecting, open => { if (!open) railExpanded.value = false; });
function toggleRail() {
  if (railSlim.value) { railExpanded.value = true; return; }
  railOpen.value = !railOpen.value;
}

/**
 * Where opening a test lands. Beside docked details the failing check is selected, because that is what
 * the reader came for. As a sheet it would cover a small screen with what the failure card already says.
 */
function landing(test: TestTrace): { span: string } | undefined {
  const failing = test.failure?.span;
  return inspectorDocks.value && failing ? { span: failing.id } : undefined;
}

/** A test's address from the list: the view the reader is in, landing on its failure where details dock. */
function testHref(test: TestTrace): string {
  const current = route.value;
  return href({ name: "test", testId: test.id, view: current.name === "test" ? current.view : "steps", selection: landing(test) });
}

const testTabs = computed(() => {
  const test = selectedTest.value;
  const current = route.value;
  if (!test || current.name !== "test") return [];
  const views: [TestViewId, string][] = [["steps", "Steps"], ["timeline", "Timeline"], ["state", "State"], ["evidence", "Evidence"]];
  return views.map(([id, label]) => ({ id, label, href: href({ name: "test", testId: test.id, view: id, selection: current.selection }) }));
});

// The strip is a tab list: clicking a tab follows its link, and the arrow keys emit the same choice.
function selectTab(id: string) {
  const current = route.value;
  if (current.name === "test") navigate({ ...current, view: id as TestViewId });
}
const runView = computed<RunViewId>(() => route.value.name === "run" ? route.value.view ?? "overview" : "overview");
function selectRunTab(id: string) {
  navigate({ name: "run", view: id as RunViewId });
}

/** The tests either side of this one, in the list's current filter and order. */
const neighbours = computed(() => {
  const test = selectedTest.value;
  if (!test || !run.value) return {};
  const list = orderedTests(run.value.tests);
  const index = list.indexOf(test);
  if (index < 0) return {};
  const link = (entry: TestTrace | undefined) => entry ? { test: entry, href: testHref(entry) } : undefined;
  return { previous: link(list[index - 1]), next: link(list[index + 1]) };
});

/** Where the reader is: the trace, the test, and what the details show. Each step is a link back to it. */
const trail = computed<Crumb[]>(() => {
  if (!run.value) return [];
  const crumbs: Crumb[] = [{ label: fileName.value || "Run", href: "#/", title: `${fileName.value}: the run` }];
  const test = selectedTest.value;
  const current = route.value;
  if (test && current.name === "test") crumbs.push({ label: `${pad(test.number)} ${testTitle(test)}`, href: href({ name: "test", testId: test.id, view: current.view }) });
  const selected = selectedSpan.value?.name ?? (selectedItem.value ? itemTitle(selectedItem.value) : undefined);
  if (selected) crumbs.push({ label: selected, href: href(current) });
  return crumbs;
});

function openPicker() { fileInput.value?.click(); }
function showTest(test: TestTrace, selection?: { span: string }) {
  const current = route.value;
  // Switching test keeps the view the reader chose; only a first visit lands on the steps.
  navigate({ name: "test", testId: test.id, view: current.name === "test" ? current.view : "steps", selection: selection ?? landing(test) });
  // On a narrow screen the test list is a drawer: picking a test is the reason it was opened.
  if (!railFits.value) railOpen.value = false;
}
function showRun() {
  navigate({ name: "run" });
  if (!railFits.value) railOpen.value = false;
}
function selectSpan(span: Span | undefined) {
  const current = route.value;
  if (span?.test && span.test !== selectedTest.value) { showTest(span.test, { span: span.id }); return; }
  if (span && current.name === "test" && run.value?.spans.includes(span)) { navigate({ name: "run", view: "operations", selection: { span: span.id } }); return; }
  replace({ ...current, selection: span ? { span: span.id } : undefined });
}
function selectItem(item: Item) {
  const current = route.value;
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
    resetTestFilter();
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
  source.value = { kind: "file" };
  syncQuery("");
}

/* The address names the open trace, so a demo or hosted trace is shareable from the bar. */
function syncQuery(query: string) {
  try {
    history.replaceState(null, "", `${location.pathname}${query}${location.hash}`);
  } catch { /* The address stays as it was; the copy control still shares the open trace. */ }
}

/* The shared link keeps the reader's hash, so a link to a failing check shares as one. */
const shareLink = computed(() => {
  void route.value;
  return source.value ? shareUrl(location.origin, location.pathname, source.value, location.hash) : null;
});

/* The facts under each demo come from the traces themselves, read once while the start shows. */
async function loadDemoFacts() {
  const missing = demos.filter(entry => !(entry.key in demoFacts.value));
  await Promise.all(missing.map(async entry => {
    try {
      const response = await fetch(demoFileUrl(entry));
      if (!response.ok) throw new Error(`The demo trace could not be loaded (${response.status}).`);
      demoFacts.value[entry.key] = await summarizeDemo(await response.arrayBuffer());
    } catch {
      demoFacts.value[entry.key] = null;
    }
  }));
}

async function loadDemo(key = "full") {
  loading.value = true;
  try {
    const entry = resolveDemo(key);
    const response = await fetch(demoFileUrl(entry));
    if (!response.ok) throw new Error(`The demo trace could not be loaded (${response.status}).`);
    await loadBuffer(await response.arrayBuffer(), entry.label);
    source.value = { kind: "demo", key: entry.key };
    syncQuery(`?demo=${entry.key === "full" ? "1" : entry.key}`);
    if (entry.key !== "full" && run.value?.tests.length === 1) {
      replace({ name: "test", testId: run.value.tests[0].id, view: "steps" });
    }
  } catch (reason) {
    problem.value = { kind: "load", message: reason instanceof Error ? reason.message : "The demo trace could not be opened." };
    loading.value = false;
  }
}
async function loadTraceUrl(url: string) {
  loading.value = true;
  try {
    const response = await fetch(url);
    if (!response.ok) throw new Error(`The trace at ${url} responded with ${response.status}.`);
    await loadBuffer(await response.arrayBuffer(), traceNameFromUrl(url));
    source.value = { kind: "remote", url };
    syncQuery(`?trace=${encodeURIComponent(url)}`);
  } catch (reason) {
    if (reason instanceof TraceOpenError) {
      problem.value = { kind: reason.problem, message: reason.message };
    } else if (reason instanceof TypeError) {
      problem.value = {
        kind: "load",
        message: `The trace at ${url} could not be fetched. The server may block cross-origin reads, the link may be wrong, or the network may be down. A downloaded copy still opens by dropping it in.`
      };
    } else {
      problem.value = { kind: "load", message: reason instanceof Error ? reason.message : `The trace at ${url} could not be loaded.` };
    }
    loading.value = false;
  }
}
function fileChanged(event: Event) {
  const file = (event.target as HTMLInputElement).files?.[0];
  if (file) void loadFile(file);
}
// ?demo=<key> opens a bundled demo (?demo=1 is the full one); ?trace=<absolute-url> fetches a hosted trace.
const query = new URLSearchParams(location.search);
const requestedDemo = query.get("demo");
const requestedTrace = parseTraceParam(query.get("trace"));
if (requestedTrace.state === "valid") void loadTraceUrl(requestedTrace.url);
else if (requestedTrace.state === "invalid") {
  problem.value = {
    kind: "load",
    message: `“${requestedTrace.value}” is not a usable trace link. Share an absolute http(s) URL to a .prototrace file.`
  };
} else if (requestedDemo !== null) void loadDemo(requestedDemo);
onMounted(() => { if (!run.value) void loadDemoFacts(); });
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
  <AppHeader :share="shareLink" :trail="trail" :rail="run ? { open: railOpen && !railSlim } : undefined"
             @open="openPicker" @toggle-rail="toggleRail" />
  <input ref="fileInput" type="file" accept=".prototrace,application/zip" hidden @change="fileChanged">
  <main>
    <section v-if="!run" class="empty-state">
      <div class="start-panel" :class="{ 'demos-open': demosOpen }">
        <AppButton variant="icon" class="demos-toggle" :label="demosOpen ? 'Hide the demos' : 'Show the demos'"
                   @click="demosOpen = !demosOpen">
          <Icon name="sidebar" />
        </AppButton>

        <div class="drop-zone" :class="{ dragging }" @click="openPicker"
             @dragover.prevent="dragging = true" @dragleave="dragging = false" @drop.prevent="dropped">
          <BrandMark :size="72" />
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
            </div>
          </template>
          <template v-else>
            <h1>Open a ProtoTest execution</h1>
            <p v-if="dragging">Drop it to open the trace.</p>
            <p v-else>Drop a <code>.prototrace</code> file, or the trace artifact a pull request run uploaded, here or choose one. It opens in this browser and is never uploaded.</p>
            <div class="empty-actions">
              <AppButton variant="primary" @click.stop="openPicker">Choose trace file</AppButton>
            </div>
            <small>Your files are read in this browser. Nothing is uploaded.</small>
          </template>
        </div>

        <section v-if="demosOpen" class="demos" aria-label="Bundled demos">
          <h2>Or start from a demo</h2>
          <DemoList :facts="demoFacts" @open="loadDemo" />
        </section>
      </div>
    </section>

    <section v-else class="workspace" :class="{ railed: railColumn, slim: railSlim, docked: inspecting && inspectorDocks, sheet: inspecting && !inspectorDocks }"
             :style="{
               '--rail-width': railResize.width.value ? `${railResize.width.value}px` : undefined,
               '--inspector-width': inspectorResize.width.value ? `${inspectorResize.width.value}px` : undefined,
               '--inspector-sheet-height': sheetHeight === null ? undefined : `${sheetHeight}px`
             }">
      <a class="skip" href="#workspace-view">Skip to the view</a>

      <button v-if="railDrawer" class="rail-scrim" type="button" aria-label="Close the test list" @click="railOpen = false" />
      <TestRail v-if="railColumn || railDrawer" :class="{ drawer: railDrawer }" :tests="run.tests" :counts="run.counts"
                :selected="selectedTest" :at-run="!selectedTest && !missingTestId" :slim="railSlim" :href-for="testHref"
                @select="showTest" @run="showRun" @expand="railExpanded = true" />
      <ColumnResizer v-if="railColumn && !railSlim" class="rail-resizer" label="Resize the test list"
                     :min="railResize.min" :max="railResize.max" :now="railResize.width.value" edge="leading"
                     @start="railResize.start" @nudge="railResize.nudge" @reset="railResize.reset" />

      <div ref="viewHost" class="view-host" id="workspace-view" tabindex="-1">
        <div v-if="missingTestId" class="missing-test">
          <h2>This trace has no such test</h2>
          <p>The link names <code>{{ missingTestId }}</code>, which is not in {{ fileName }}. It may come from another run.</p>
          <AppButton variant="primary" @click="navigate({ name: 'run' })">Back to the run</AppButton>
        </div>
        <template v-else>
          <!-- Cached so returning from a test keeps the run list instead of rebuilding it; deactivation detaches its DOM. -->
          <KeepAlive>
            <RunView v-if="!selectedTest" :run="run" :file-name="fileName" :selected-span="selectedSpan" :selected-item="selectedItem"
                     :view="runView" @tab="selectRunTab" @select="showTest" @span="selectSpan" @item="selectItem" @artifact="openArtifact = $event" />
          </KeepAlive>
          <TestView v-if="selectedTest" :test="selectedTest" :view="view" :tabs="testTabs"
                    :selected-span="selectedSpan" :selected-item="selectedItem"
                    :previous="neighbours.previous" :next="neighbours.next"
                    @select-span="selectSpan" @select-item="selectItem" @artifact="openArtifact = $event" @tab="selectTab" />
        </template>
      </div>

      <ColumnResizer v-if="inspecting && inspectorDocks" class="inspector-resizer" label="Resize the details"
                     :min="inspectorResize.min" :max="inspectorResize.max" :now="inspectorResize.width.value" edge="trailing"
                     @start="inspectorResize.start" @nudge="inspectorResize.nudge" @reset="inspectorResize.reset" />
      <Inspector v-if="inspecting" :test="selectedTest ?? run" :span="selectedSpan" :item="selectedItem"
                 @select="selectSpan" @item="selectItem" @artifact="openArtifact = $event" @close="selectSpan(undefined)" />
      <button v-if="inspecting && !inspectorDocks" class="inspector-handle" type="button"
              aria-label="Resize the details" title="Drag to resize, tap to expand"
              @pointerdown="startSheetDrag" @keydown.up.prevent="nudgeSheet(64)" @keydown.down.prevent="nudgeSheet(-64)" />
    </section>

    <ArtifactOverlay :artifact="openArtifact" :read-artifact="readArtifact" @close="openArtifact = undefined" />
  </main>
</template>

<style scoped>
main { flex: 1 1 auto; min-height: 0; display: grid; grid-template-rows: minmax(0, 1fr); }
/*
 * One row of columns: the test list, the view, the details. Which of them are columns is decided in the
 * script (the widths change behaviour); the defaults below are clamps, and the reader can drag them.
 */
.workspace {
  --rail-default: clamp(220px, 18vw, 290px);
  --rail-slim: 56px;
  --inspector-default: clamp(340px, 27vw, 460px);
  min-width: 0;
  min-height: 0;
  /* No bottom padding: the view scrolls to the window's edge, and keeps its breathing room inside the scroll. */
  padding: var(--space-3) clamp(10px, 1.4vw, 20px) 0;
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  grid-template-rows: minmax(0, 1fr);
  gap: var(--space-3);
}
.workspace.railed { grid-template-columns: var(--rail-width, var(--rail-default)) 7px minmax(0, 1fr); }
.workspace.railed.slim { grid-template-columns: var(--rail-slim) minmax(0, 1fr); }
.workspace.docked { grid-template-columns: minmax(0, 1fr) 7px var(--inspector-width, var(--inspector-default)); }
.workspace.railed.docked {
  grid-template-columns: var(--rail-width, var(--rail-default)) 7px minmax(0, 1fr) 7px var(--inspector-width, var(--inspector-default));
}
.workspace.railed.slim.docked { grid-template-columns: var(--rail-slim) minmax(0, 1fr) 7px var(--inspector-width, var(--inspector-default)); }
/* The one scroller for a view. Its content runs to the bottom edge of the window and ends with its own
   margin, so a scrolled view is cut by the window - not by a strip of background above it. */
.view-host { min-width: 0; min-height: 0; padding-bottom: var(--space-4); overflow: auto; overscroll-behavior: contain; scrollbar-gutter: stable; }
/* The rail and the docked inspector scroll inside themselves, so they keep the gap the view scrolls into. */
.workspace > .rail { margin-bottom: var(--space-4); }
.workspace.docked > .inspector { margin-bottom: var(--space-4); }
/* Keyboard readers jump past the rail straight to the view. */
.skip { position: fixed; z-index: 60; top: var(--space-2); left: var(--space-2); padding: var(--space-2) var(--space-3); border: 1px solid var(--blueprint); border-radius: var(--radius-control); background: var(--surface); font-size: var(--text-meta); transform: translateY(-300%); }
.skip:focus-visible { transform: none; }

/* Below the column width the test list is a drawer over the view; picking a test or the scrim closes it. */
.workspace > .rail.drawer {
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
.rail-scrim { position: fixed; z-index: 50; inset: 0; padding: 0; border: 0; background: color-mix(in srgb, var(--pt-navy-abyss) 48%, transparent); cursor: default; }

/* Below the docking width the details are a sheet with a definite height, so its body is always what scrolls. */
.workspace.sheet > .inspector {
  position: fixed;
  z-index: 40;
  inset: auto 0 0 0;
  height: var(--inspector-sheet-height, min(62dvh, 560px));
  max-height: 94dvh;
  border-bottom: 0;
  border-radius: var(--radius-overlay) var(--radius-overlay) 0 0;
  box-shadow: var(--elevation-overlay);
}
/* Under a sheet, the view keeps the sheet's height free at its end and scrolls a selection above it, so
   nothing the reader picked hides behind the details it opened. */
.workspace.sheet > .view-host {
  padding-bottom: calc(var(--inspector-sheet-height, min(62dvh, 560px)) + var(--space-4));
  scroll-padding-bottom: calc(var(--inspector-sheet-height, min(62dvh, 560px)) + var(--space-4));
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

.empty-actions { display: flex; flex-wrap: wrap; gap: var(--space-3); justify-content: center; }
/* The start panel's one control: the demos sidebar can be put away, and this brings it back. */
.demos-toggle { position: absolute; top: var(--space-3); right: var(--space-3); z-index: 1; }
.demos { display: grid; gap: var(--space-3); text-align: left; }
.demos h2 { font-family: var(--font-ui); font-size: var(--text-title); font-weight: var(--weight-bold); color: var(--text); }
.drop-zone h1 { margin-top: var(--space-3); }
.drop-zone code { padding: 1px var(--space-2); border-radius: var(--radius-chip); background: var(--surface-2); font-family: var(--font-mono); font-size: var(--text-meta); }
.drop-zone .problem { color: var(--danger); }
.missing-test { max-width: 560px; margin: var(--space-6) auto; padding: var(--space-5); display: grid; gap: var(--space-3); justify-items: start; border: 1px dashed var(--border-strong); border-radius: var(--radius-panel); }
.missing-test h2 { margin: 0; font-size: var(--text-title); }
.missing-test p { margin: 0; color: var(--muted); overflow-wrap: anywhere; }
.missing-test code { font-family: var(--font-mono); font-size: var(--text-meta); }
</style>
