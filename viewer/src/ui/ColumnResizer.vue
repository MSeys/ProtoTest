<script setup lang="ts">
defineProps<{ label: string }>();
const emit = defineEmits<{ start: [event: PointerEvent]; reset: [] }>();
</script>

<template>
  <div class="resizer" role="separator" aria-orientation="vertical" :aria-label="label" tabindex="0"
       @pointerdown="emit('start', $event)" @dblclick="emit('reset')"
       @keydown.enter.prevent="emit('reset')">
    <i aria-hidden="true" />
  </div>
</template>

<style scoped>
/* A hairline at rest, a blueprint line under the pointer. Double-click puts the column back to its default. */
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
