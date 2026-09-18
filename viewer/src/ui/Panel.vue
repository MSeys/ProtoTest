<script setup lang="ts">
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
          <h2 v-if="title">{{ title }}</h2>
          <p v-if="subtitle">{{ subtitle }}</p>
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
.head.sticky { position: sticky; top: 0; z-index: 1; }
.titles { min-width: 0; display: flex; align-items: center; gap: var(--space-3); }
.text { min-width: 0; display: grid; }
h2 { font-family: var(--font-ui); font-size: var(--text-strong); font-weight: var(--weight-bold); letter-spacing: 0; line-height: var(--leading-tight); }
p { color: var(--muted); font-size: var(--text-meta); line-height: var(--leading); }
.actions { flex: 1 1 auto; min-width: 0; display: flex; flex-wrap: wrap; align-items: center; justify-content: flex-end; gap: var(--space-2); }
.body { min-width: 0; }
.body.normal { padding: var(--space-4); }
.body.tight { padding: var(--space-2); }
.body.none { padding: 0; }
</style>
