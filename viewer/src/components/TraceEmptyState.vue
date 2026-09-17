<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from "vue";
import logoLight from "../../../assets/brand/prototest-mark.svg?url";
import logoDark from "../../../assets/brand/prototest-mark-white.svg?url";

defineProps<{ error?: string }>();
const emit = defineEmits<{ open: []; demo: []; file: [file: File] }>();

type Theme = "light" | "dark";
const theme = ref<Theme>(
  (document.documentElement.dataset.theme as Theme)
  ?? (matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light")
);
const logoUrl = computed(() => theme.value === "dark" ? logoDark : logoLight);

// Keep in sync when AppHeader toggles the theme
const observer = new MutationObserver(() => {
  const t = document.documentElement.dataset.theme as Theme | undefined;
  if (t) theme.value = t;
});
onMounted(() => observer.observe(document.documentElement, { attributes: true, attributeFilter: ["data-theme"] }));
onUnmounted(() => observer.disconnect());

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
