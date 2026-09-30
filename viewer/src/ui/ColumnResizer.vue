<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { visibleSibling } from "./useColumnResize";

const props = defineProps<{
  label: string;
  min: number;
  max: number;
  /** The width the column has now, when the reader set one; otherwise the rendered width is measured. */
  now?: number;
  /** Which side the column sits on: the rail leads, the inspector trails. */
  edge: "leading" | "trailing";
}>();
const emit = defineEmits<{ start: [event: PointerEvent]; reset: []; nudge: [event: KeyboardEvent, pixels: number] }>();

// The arrow keys resize by one step, positive to the right, and the parent maps that onto its column.
function keydown(event: KeyboardEvent) {
  if (event.key === "ArrowLeft") { event.preventDefault(); emit("nudge", event, -16); }
  else if (event.key === "ArrowRight") { event.preventDefault(); emit("nudge", event, 16); }
  else if (event.key === "Enter") { event.preventDefault(); emit("reset"); }
}

// A focused splitter reports its position like the window-splitter pattern: the set width, or the width
// the column actually rendered at before the reader ever dragged it.
const root = ref<HTMLElement>();
const measured = ref<number | null>(null);
function measure() {
  const column = root.value ? visibleSibling(root.value, props.edge === "leading" ? "previous" : "next") : null;
  const width = column?.getBoundingClientRect().width;
  if (width) measured.value = width;
}
onMounted(measure);
const value = computed(() => Math.round(props.now ?? measured.value ?? props.min));
</script>

<template>
  <div ref="root" class="resizer" role="separator" aria-orientation="vertical" :aria-label="label"
       :aria-valuemin="min" :aria-valuemax="max" :aria-valuenow="value" :aria-valuetext="`${value} pixels wide`"
       tabindex="0" @pointerdown="emit('start', $event)" @dblclick="emit('reset')" @keydown="keydown" @focus="measure">
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
