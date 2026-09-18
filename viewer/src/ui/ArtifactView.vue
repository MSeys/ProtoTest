<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from "vue";
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
const artifactUrl = ref("");
const artifactText = ref("");
const artifactError = ref("");
const artifactLoading = ref(false);
let artifactLoad = 0;
const isJson = computed(() => props.artifact.mediaType.includes("json"));
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
        <AppButton :disabled="!artifactUrl" @click="openArtifact">Open</AppButton>
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
