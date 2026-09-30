<script setup lang="ts">
import { useId } from "vue";

/*
 * A short explanation behind a small mark: shown on hover or keyboard focus, in the viewer's own style, and read
 * by a screen reader as the description of what it sits beside. No native title, so no delay and no OS styling.
 */
defineProps<{ text: string }>();
const id = useId();
</script>

<template>
  <span class="hint">
    <button type="button" class="mark" :aria-describedby="id" aria-label="What this shows">?</button>
    <span :id="id" role="tooltip" class="tip">{{ text }}</span>
  </span>
</template>

<style scoped>
.hint { position: relative; display: inline-flex; }
.mark {
  width: 16px;
  height: 16px;
  padding: 0;
  display: inline-grid;
  place-items: center;
  border: 1px solid var(--border-strong);
  border-radius: 50%;
  background: transparent;
  color: var(--dim);
  font: var(--weight-semibold) var(--text-micro) var(--font-ui);
  line-height: 1;
  cursor: default;
}
.mark:hover, .mark:focus-visible { border-color: var(--muted); color: var(--text); }
.tip {
  position: absolute;
  top: calc(100% + var(--space-2));
  /* Titles sit at the panel's left edge, so the tip opens rightward from the mark instead of centring on it. */
  left: calc(var(--space-2) * -1);
  z-index: 20;
  width: max-content;
  max-width: min(280px, 60vw);
  padding: var(--space-2) var(--space-3);
  border: 1px solid var(--border-strong);
  border-radius: var(--radius-control);
  background: var(--surface-2);
  box-shadow: var(--elevation-overlay);
  color: var(--text);
  font: var(--weight-regular) var(--text-meta) / var(--leading) var(--font-ui);
  letter-spacing: 0;
  white-space: normal;
  pointer-events: none;
  opacity: 0;
  visibility: hidden;
  transform: translateY(-2px);
  transition: opacity var(--motion-fast) var(--motion-ease), transform var(--motion-fast) var(--motion-ease), visibility var(--motion-fast);
}
.mark:hover + .tip, .mark:focus-visible + .tip { opacity: 1; visibility: visible; transform: none; }
</style>
