<script setup lang="ts">
defineProps<{ label: string }>();
const emit = defineEmits<{ start: [event: PointerEvent]; reset: []; nudge: [event: KeyboardEvent, pixels: number] }>();

// The arrow keys resize by one step, positive to the right, and the parent maps that onto its column.
function keydown(event: KeyboardEvent) {
  if (event.key === "ArrowLeft") { event.preventDefault(); emit("nudge", event, -16); }
  else if (event.key === "ArrowRight") { event.preventDefault(); emit("nudge", event, 16); }
  else if (event.key === "Enter") { event.preventDefault(); emit("reset"); }
}
</script>

<template>
  <div class="resizer" role="separator" aria-orientation="vertical" :aria-label="label" tabindex="0"
       @pointerdown="emit('start', $event)" @dblclick="emit('reset')" @keydown="keydown">
    <i aria-hidden="true" />
  </div>
</template>

<style scoped>
/* A hairline at rest, a blueprint line under the pointer. Double-click or Enter puts the column back to
   its default; the arrow keys move it a step at a time. */
.resizer { position: relative; display: grid; place-items: center; cursor: col-resize; touch-action: none; }
.resizer i {
  width: 1px;
  height: 100%;
  background: var(--border);
  transition: background var(--motion-fast) var(--motion-ease), box-shadow var(--motion-fast) var(--motion-ease);
}
.resizer:hover i, .resizer:focus-visible i { width: 2px; background: var(--blueprint); box-shadow: 0 0 0 2px var(--blueprint-soft); }
.resizer:focus-visible { outline: 0; }
</style>
