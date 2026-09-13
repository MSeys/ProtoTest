<script setup lang="ts">
import type { TraceArtifact } from "../trace-schema";
import ArtifactViewer from "./ArtifactViewer.vue";
import { useHorizontalResize } from "../use-horizontal-resize";

defineProps<{ artifact?: TraceArtifact; readArtifact?: (artifact: TraceArtifact) => Promise<Blob> }>();
const emit = defineEmits<{ close: [] }>();
const panel = useHorizontalResize("prototrace.inspector-width", 480, 360, () => Math.min(900, window.innerWidth - 280), -1);
</script>

<template>
  <div v-if="artifact" class="drawer-backdrop" @click.self="emit('close')">
    <aside class="detail-panel run-artifact-panel" :style="{ width: `${panel.size.value}px` }" role="dialog" aria-modal="true" aria-label="Run artifact">
      <div class="drawer-resizer" role="separator" aria-label="Resize inspector" aria-orientation="vertical" @pointerdown="panel.startResize" />
      <div class="panel-heading inspector-heading"><div><strong>Run output</strong><span>{{ artifact.mediaType }}</span></div><button type="button" aria-label="Close artifact" @click="emit('close')">×</button></div>
      <div class="detail-content"><ArtifactViewer :artifact="artifact" :read-artifact="readArtifact" /></div>
    </aside>
  </div>
</template>
