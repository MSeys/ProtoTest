<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from "vue";
import type { Artifact as TraceArtifact } from "../trace/model";
import AppButton from "./AppButton.vue";
import EmptyState from "./EmptyState.vue";

const props = defineProps<{
  artifact: TraceArtifact;
  readArtifact?: (artifact: TraceArtifact) => Promise<Blob>;
  /**
   * Fill the parent's height instead of taking a fixed one. The overlay uses it so the preview is the only
   * thing that scrolls; inline, in the inspector, the preview keeps a fixed height inside a scrolling panel.
   */
  fill?: boolean;
}>();
const TRACE_VIEWER = "https://trace.playwright.dev";

const artifactUrl = ref("");
const artifactBlob = ref<Blob>();
const handoff = ref<"idle" | "opening" | "blocked" | "failed">("idle");
const traceFrame = ref<HTMLIFrameElement>();
// Playwright's viewer is the only thing this application ever loads from elsewhere, so it loads when asked
// for and not before: until then the viewer has made no request outside the file you opened.
const embedViewer = ref(false);
const artifactText = ref("");
const artifactError = ref("");
const artifactLoading = ref(false);
let artifactLoad = 0;
const isJson = computed(() => props.artifact.mediaType.includes("json"));
// Playwright names its own trace; a zip called …-trace.zip from an older run counts too.
const isPlaywrightTrace = computed(() => props.artifact.mediaType.includes("playwright.trace")
  || (props.artifact.mediaType.includes("zip") && /-trace\.zip$/i.test(props.artifact.name)));
const formattedJson = computed(() => {
  if (!isJson.value || !artifactText.value) return "";
  try { return JSON.stringify(JSON.parse(artifactText.value), null, 2); }
  catch { return artifactText.value; }
});

function releaseArtifactUrl() {
  if (artifactUrl.value) URL.revokeObjectURL(artifactUrl.value);
  artifactUrl.value = "";
}
async function loadArtifact() {
  const load = ++artifactLoad;
  releaseArtifactUrl();
  artifactText.value = "";
  artifactError.value = "";
  if (!props.readArtifact) return;
  if (props.artifact.error) {
    artifactError.value = props.artifact.error;
    return;
  }
  artifactLoading.value = true;
  try {
    const blob = await props.readArtifact(props.artifact);
    if (load !== artifactLoad) return;
    artifactBlob.value = blob;
    artifactUrl.value = URL.createObjectURL(blob);
    if (props.artifact.mediaType.startsWith("text/") || props.artifact.mediaType.includes("json") || props.artifact.mediaType.includes("xml"))
      artifactText.value = await blob.text();
  } catch (reason) {
    if (load !== artifactLoad) return;
    artifactError.value = reason instanceof Error ? reason.message : "The artifact could not be read.";
  } finally {
    if (load === artifactLoad) artifactLoading.value = false;
  }
}
function downloadArtifact() {
  if (!artifactUrl.value) return;
  const anchor = document.createElement("a");
  anchor.href = artifactUrl.value;
  anchor.download = props.artifact.name;
  anchor.click();
}
function openArtifact() {
  if (artifactUrl.value) window.open(artifactUrl.value, "_blank", "noopener,noreferrer");
}

/**
 * Playwright's own viewer reads a trace in the browser: wherever it runs - embedded here or in its own tab -
 * it announces itself with a ready message and then takes the file over postMessage. Nothing is uploaded, and
 * the window cannot be opened with noopener: that is the channel it answers on.
 *
 * Both the frame below and a new tab answer on this one listener; a trace that arrives before its viewer is
 * ready is posted again as soon as that viewer says so.
 */
function isTraceViewer(event: MessageEvent): boolean {
  return event.origin === TRACE_VIEWER && (event.data as { method?: string } | null)?.method === "ready";
}

function send(target: Window | null | undefined) {
  if (!target || !artifactBlob.value) return false;
  target.postMessage({ method: "load", params: { trace: artifactBlob.value } }, TRACE_VIEWER);
  return true;
}

function onViewerReady(event: MessageEvent) {
  if (!isTraceViewer(event)) return;
  if (send(event.source as Window | null) && handoff.value === "opening") handoff.value = "idle";
}

onMounted(() => window.addEventListener("message", onViewerReady));
onBeforeUnmount(() => window.removeEventListener("message", onViewerReady));

// The embedded viewer only asks once, so a trace that is still being read is posted when it arrives.
watch(artifactBlob, blob => {
  if (blob && isPlaywrightTrace.value) send(traceFrame.value?.contentWindow);
});

function showEmbeddedViewer() {
  embedViewer.value = true;
}

function openInPlaywright() {
  const target = window.open(TRACE_VIEWER, "_blank");
  if (!target) {
    handoff.value = "blocked";
    return;
  }

  handoff.value = "opening";
  // The tab answers on the shared listener; if it never does, say so rather than leaving a spinner.
  setTimeout(() => { if (handoff.value === "opening") handoff.value = "failed"; }, 20_000);
}

watch(() => props.artifact.id, () => void loadArtifact(), { immediate: true });
onBeforeUnmount(releaseArtifactUrl);
</script>

<template>
  <section class="artifact" :class="{ fill }">
    <header>
      <div>
        <strong>{{ artifact.name }}</strong>
        <span>{{ artifact.description || artifact.mediaType }}</span>
      </div>
      <div class="actions">
        <AppButton v-if="isPlaywrightTrace" :disabled="!artifactBlob || handoff === 'opening'" @click="openInPlaywright">
          {{ handoff === "opening" ? "Opening…" : "Open in a tab ↗" }}
        </AppButton>
        <AppButton v-else :disabled="!artifactUrl" @click="openArtifact">Open</AppButton>
        <AppButton variant="primary" :disabled="!artifactUrl" @click="downloadArtifact">Download</AppButton>
        <slot name="actions" />
      </div>
    </header>

    <EmptyState v-if="artifactLoading" message="Loading artifact…" />
    <div v-else-if="artifactError" class="failure"><strong>Artifact unavailable</strong><p>{{ artifactError }}</p></div>
    <img v-else-if="artifactUrl && artifact.mediaType.startsWith('image/')" :src="artifactUrl" :alt="artifact.name" class="image">
    <video v-else-if="artifactUrl && artifact.mediaType.startsWith('video/')" :src="artifactUrl" class="media" controls />
    <audio v-else-if="artifactUrl && artifact.mediaType.startsWith('audio/')" :src="artifactUrl" class="audio" controls />
    <iframe v-else-if="artifactUrl && artifact.mediaType === 'application/pdf'" :src="artifactUrl" :title="artifact.name" class="frame" sandbox="" />
    <iframe v-else-if="artifactUrl && artifact.mediaType === 'text/html'" :src="artifactUrl" :title="artifact.name" class="frame" sandbox="allow-scripts" />
    <pre v-else-if="formattedJson" class="text json"><code>{{ formattedJson }}</code></pre>
    <pre v-else-if="artifactText" class="text">{{ artifactText }}</pre>
    <!-- Playwright's viewer: it reads the trace in this browser, and only once you ask for it. -->
    <div v-else-if="isPlaywrightTrace" class="playwright" :class="{ embedded: embedViewer }">
      <iframe v-if="embedViewer" ref="traceFrame" :src="TRACE_VIEWER" title="Playwright trace viewer" class="frame"
              @load="send(traceFrame?.contentWindow)" />
      <div v-else class="offer">
        <strong>A Playwright trace</strong>
        <p>
          Every browser action with its DOM snapshots, network traffic and console. Playwright's own viewer reads
          it; this trace is handed to that page inside this browser and is not uploaded.
        </p>
        <AppButton variant="primary" :disabled="!artifactBlob" @click="showEmbeddedViewer">Show it here</AppButton>
      </div>
      <small class="source">
        {{ embedViewer ? "Rendered by trace.playwright.dev inside this browser; the file is not uploaded."
                       : "Showing it loads trace.playwright.dev — the only page this viewer fetches from elsewhere." }}
      </small>
      <p v-if="handoff === 'blocked'" class="warn">The tab did not open. Allow pop-ups for this page, or use the embedded viewer above.</p>
      <p v-else-if="handoff === 'failed'" class="warn">
        The tab did not answer. Download the trace and drop it onto trace.playwright.dev.
      </p>
    </div>
    <EmptyState v-else :message="`Preview is not available for ${artifact.mediaType}. Download the bundled file to inspect it.`" />
  </section>
</template>

<style scoped>
.artifact { min-width: 0; display: grid; gap: var(--space-4); align-content: start; }
header { display: flex; flex-wrap: wrap; align-items: flex-start; justify-content: space-between; gap: var(--space-4); }
header > div:first-child { min-width: 0; display: flex; flex-direction: column; }
header strong { overflow-wrap: anywhere; font-size: var(--text-strong); }
header span { color: var(--muted); font-size: var(--text-meta); }
.actions { flex: none; display: flex; gap: var(--space-2); }
.failure { padding: var(--space-4); border: 1px solid var(--danger); border-radius: var(--radius-control); background: var(--danger-soft); font-size: var(--text-meta); }
.failure strong { display: block; color: var(--danger); }
.failure p { margin-top: var(--space-1); color: var(--muted); }
.image { display: block; max-width: 100%; max-height: 60vh; margin: 0 auto; border: 1px solid var(--border); border-radius: var(--radius-control); background: var(--surface-2); }
/* Inline, text runs its full length and the surrounding view scrolls; in the overlay (.fill) it scrolls itself. */
.text {
  margin: 0;
  padding: var(--space-4);
  border: 1px solid var(--border);
  border-radius: var(--radius-control);
  background: var(--surface-sunken);
  color: var(--text-on-sunken);
  font: var(--text-meta)/var(--leading) var(--font-mono);
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
.json { tab-size: 2; }
.playwright { min-width: 0; min-height: 0; display: grid; gap: var(--space-2); align-content: start; }
.playwright.embedded { align-content: stretch; grid-template-rows: minmax(0, 1fr) auto; }
.offer { padding: var(--space-4); display: grid; gap: var(--space-3); justify-items: start; border: 1px solid var(--border); border-radius: var(--radius-control); background: var(--surface-2); }
.offer p { margin: 0; color: var(--muted); font-size: var(--text-meta); }
.playwright p { margin: 0; color: var(--warning); font-size: var(--text-meta); }
.playwright .source { color: var(--dim); font-size: var(--text-micro); }
.fill .playwright { height: 100%; }
.json code { padding: 0; background: transparent; color: inherit; font: inherit; }
.media, .frame { display: block; width: 100%; border: 1px solid var(--border); border-radius: var(--radius-control); background: var(--surface-2); }
.media { max-height: 60vh; }
.frame { height: min(72vh, 760px); min-height: 360px; }
.audio { width: 100%; }

/* Filling: the preview takes every pixel under the header and is the single scroller. */
.artifact.fill { height: 100%; grid-template-rows: auto minmax(0, 1fr); align-content: stretch; }
.fill .text, .fill .frame, .fill .media { height: 100%; min-height: 0; max-height: none; }
.fill .text { overflow: auto; }
.fill .image { width: 100%; height: 100%; max-height: none; object-fit: contain; }
</style>
