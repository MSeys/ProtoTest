<script setup lang="ts">
import HintTip from "./HintTip.vue";

/* The subtitle explains the panel once, for whoever asks: behind a small mark beside the title, not as a
   standing line under every heading. */
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
          <h2 v-if="title">{{ title }}<HintTip v-if="subtitle" :text="subtitle" /></h2>
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

.actions { flex: 1 1 auto; min-width: 0; display: flex; flex-wrap: wrap; align-items: center; justify-content: flex-end; gap: var(--space-2); }
.body { min-width: 0; }
.body.normal { padding: var(--space-4); }
.body.tight { padding: var(--space-2); }
.body.none { padding: 0; }
</style>
