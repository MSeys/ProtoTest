<script setup lang="ts">
import logoUrl from "../../../assets/brand/prototest-mark-transparent.svg?url";

defineProps<{ error?: string }>();
const emit = defineEmits<{ open: []; demo: []; file: [file: File] }>();

function drop(event: DragEvent) {
  const file = event.dataTransfer?.files[0];
  if (file) emit("file", file);
}
</script>

<template>
  <section class="empty-state">
    <div class="drop-zone" tabindex="0" role="button" aria-label="Open a ProtoTrace file"
         @click.self="$emit('open')" @keydown.enter="$emit('open')" @keydown.space.prevent="$emit('open')"
         @dragover.prevent @drop.prevent="drop">
      <img class="empty-mark" :src="logoUrl" alt="">
      <span class="drawing-label">Trace input / local</span>
      <h1>Open a ProtoTest execution</h1>
      <p>Inspect your own <code>.prototrace</code>, or explore the bundled SaaS demo.</p>
      <div class="empty-actions">
        <button class="primary" type="button" @click.stop="$emit('open')">Choose trace file</button>
        <button class="secondary" type="button" @click.stop="$emit('demo')">Open demo trace</button>
      </div>
      <small>Your files are processed locally. Nothing is uploaded.</small>
    </div>
    <p v-if="error" class="error" role="alert">{{ error }}</p>
  </section>
</template>
