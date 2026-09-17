<script setup lang="ts">
import { ref, watch } from "vue";
import type { TraceArtifact } from "../model/trace-schema";
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
    <div v-if="artifact" class="sheet">
      <header>
        <span class="eyebrow">Bundled output</span>
        <AppButton variant="icon" label="Close artifact" @click="emit('close')">×</AppButton>
      </header>
      <div class="body">
        <ArtifactView :artifact="artifact" :read-artifact="readArtifact" />
      </div>
    </div>
  </dialog>
</template>

<style scoped>
.overlay {
  width: min(1040px, calc(100vw - var(--space-7)));
  max-width: none;
  max-height: min(86dvh, 900px);
  padding: 0;
  border: 1px solid var(--border-strong);
  border-radius: var(--radius-overlay);
  background: var(--surface);
  color: var(--text);
  box-shadow: var(--elevation-overlay);
}
.overlay::backdrop { background: color-mix(in srgb, var(--pt-navy-abyss) 72%, transparent); }
.sheet { display: grid; grid-template-rows: auto minmax(0, 1fr); max-height: inherit; }
header {
  padding: var(--space-3) var(--space-5);
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-4);
  border-bottom: 1px solid var(--border);
}
.body { min-height: 0; padding: var(--space-5); overflow: auto; }
</style>
