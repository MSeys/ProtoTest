<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from "vue";
import type { TraceArtifact } from "../trace-schema";

const props = defineProps<{
  artifact: TraceArtifact;
  readArtifact?: (artifact: TraceArtifact) => Promise<Blob>;
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
  <section class="artifact-viewer">
    <div class="artifact-heading">
      <div><span class="drawing-label">Bundled artifact</span><h2>{{ artifact.name }}</h2><p>{{ artifact.description || artifact.mediaType }}</p></div>
      <div class="artifact-actions"><button type="button" :disabled="!artifactUrl" @click="openArtifact">Open</button><button type="button" :disabled="!artifactUrl" @click="downloadArtifact">Download</button></div>
    </div>
    <div v-if="artifactLoading" class="inspector-empty">Loading artifact…</div>
    <div v-else-if="artifactError" class="error-card"><strong>Artifact unavailable</strong><p>{{ artifactError }}</p></div>
    <img v-else-if="artifactUrl && artifact.mediaType.startsWith('image/')" :src="artifactUrl" :alt="artifact.name" class="artifact-image">
    <video v-else-if="artifactUrl && artifact.mediaType.startsWith('video/')" :src="artifactUrl" class="artifact-media" controls />
    <audio v-else-if="artifactUrl && artifact.mediaType.startsWith('audio/')" :src="artifactUrl" class="artifact-audio" controls />
    <iframe v-else-if="artifactUrl && (artifact.mediaType === 'application/pdf' || artifact.mediaType === 'text/html')" :src="artifactUrl" :title="artifact.name" class="artifact-frame" sandbox="" />
    <pre v-else-if="formattedJson" class="artifact-text artifact-json"><code>{{ formattedJson }}</code></pre>
    <pre v-else-if="artifactText" class="artifact-text">{{ artifactText }}</pre>
    <div v-else class="inspector-empty">Preview is not available for {{ artifact.mediaType }}. Download the bundled file to inspect it.</div>
  </section>
</template>
