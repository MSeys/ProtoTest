<script setup lang="ts">
import { ref, watch } from "vue";
import type { Artifact as TraceArtifact } from "../trace/model";
import ArtifactView from "./ArtifactView.vue";
import AppButton from "./AppButton.vue";

const props = defineProps<{
  artifact?: TraceArtifact;
  readArtifact?: (artifact: TraceArtifact) => Promise<Blob>;
}>();
const emit = defineEmits<{ close: [] }>();
const dialog = ref<HTMLDialogElement>();

// A native dialog gives the focus trap, the backdrop and Escape without reimplementing any of them.
watch(() => props.artifact?.id, id => {
  if (id) dialog.value?.showModal();
  else dialog.value?.close();
});
</script>

<template>
  <dialog ref="dialog" class="overlay" aria-label="Artifact" @close="emit('close')" @click.self="emit('close')">
    <!-- One header, one scroller: the artifact's own title and actions head the sheet, and the preview under
         them is the only thing that scrolls. -->
    <div v-if="artifact" class="sheet">
      <ArtifactView :artifact="artifact" :read-artifact="readArtifact" fill>
        <template #actions>
          <AppButton variant="icon" label="Close artifact" @click="emit('close')">×</AppButton>
        </template>
      </ArtifactView>
    </div>
  </dialog>
</template>

<style scoped>
.overlay {
  width: min(1040px, calc(100vw - var(--space-7)));
  height: min(86dvh, 900px);
  max-width: none;
  max-height: none;
  padding: 0;
  border: 1px solid var(--border-strong);
  border-radius: var(--radius-overlay);
  background: var(--surface);
  color: var(--text);
  box-shadow: var(--elevation-overlay);
  overflow: hidden;
}
.overlay::backdrop { background: color-mix(in srgb, var(--pt-navy-abyss) 72%, transparent); }
.sheet { height: 100%; padding: var(--space-4) var(--space-5) var(--space-5); }
</style>
