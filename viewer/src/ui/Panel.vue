<script setup lang="ts">
import { useId } from "vue";

/* The subtitle explains the panel once, for whoever asks: on the title's hover and to a screen reader, not as a
   standing line under every heading. */
const hintId = useId();
defineProps<{
  title?: string;
  subtitle?: string;
  pad?: "none" | "tight" | "normal";
  /** Keeps the head in view while the panel's body scrolls past it: a phase heading over its rows. */
  sticky?: boolean;
}>();
</script>

<template>
  <section class="panel">
    <header v-if="title || $slots.actions || $slots.lead" class="head" :class="{ sticky }">
      <div class="titles">
        <slot name="lead" />
        <div class="text">
          <h2 v-if="title" :title="subtitle" :aria-describedby="subtitle ? hintId : undefined">{{ title }}<i v-if="subtitle" class="hint-mark" aria-hidden="true">?</i></h2>
          <p v-if="subtitle" :id="hintId" class="hint">{{ subtitle }}</p>
        </div>
      </div>
      <div v-if="$slots.actions" class="actions"><slot name="actions" /></div>
    </header>
    <div class="body" :class="pad ?? 'normal'"><slot /></div>
  </section>
</template>

<style scoped>
/* The one panel in the system: border only. Shadow belongs to overlays. Every panel title in the viewer is this
   head, so a reader meets one heading style for every container. */
/* Clip, not hidden: hidden would make every panel a scroll container of its own, and a sticky head inside it
   would stick to the panel instead of to the view that actually scrolls. */
.panel { border: 1px solid var(--border); border-radius: var(--radius-panel); background: var(--surface); overflow: clip; }
.head {
  min-height: var(--panel-head-height);
  padding: var(--space-2) var(--space-4);
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-2) var(--space-4);
  border-bottom: 1px solid var(--border);
  background: var(--surface);
}
/* A sticky head stops under whatever the view keeps pinned above it (the test's view tabs). */
.head.sticky { position: sticky; top: var(--sticky-offset, 0px); z-index: 1; }
.titles { min-width: 0; display: flex; align-items: center; gap: var(--space-3); }
.text { min-width: 0; display: grid; }
h2 { font-family: var(--font-ui); font-size: var(--text-strong); font-weight: var(--weight-bold); letter-spacing: 0; line-height: var(--leading-tight); }
h2 { display: inline-flex; align-items: center; gap: var(--space-2); }
.hint-mark { width: 14px; height: 14px; display: inline-grid; place-items: center; border: 1px solid var(--border-strong); border-radius: 50%; color: var(--dim); font: var(--weight-semibold) var(--text-micro) var(--font-ui); font-style: normal; line-height: 1; cursor: help; }
h2:hover .hint-mark { border-color: var(--muted); color: var(--muted); }
.hint { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); }
.actions { flex: 1 1 auto; min-width: 0; display: flex; flex-wrap: wrap; align-items: center; justify-content: flex-end; gap: var(--space-2); }
.body { min-width: 0; }
.body.normal { padding: var(--space-4); }
.body.tight { padding: var(--space-2); }
.body.none { padding: 0; }
</style>
