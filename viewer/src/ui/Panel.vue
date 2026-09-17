<script setup lang="ts">
defineProps<{ title?: string; subtitle?: string; pad?: "none" | "tight" | "normal" }>();
</script>

<template>
  <section class="panel">
    <header v-if="title || $slots.actions" class="head">
      <div class="titles">
        <strong v-if="title">{{ title }}</strong>
        <span v-if="subtitle">{{ subtitle }}</span>
      </div>
      <div v-if="$slots.actions" class="actions"><slot name="actions" /></div>
    </header>
    <div class="body" :class="pad ?? 'normal'"><slot /></div>
  </section>
</template>

<style scoped>
/* The one panel in the system: border only. Shadow belongs to overlays. */
.panel { border: 1px solid var(--border); border-radius: var(--radius-panel); background: var(--surface); overflow: hidden; }
.head {
  min-height: var(--panel-head-height);
  padding: var(--space-2) var(--space-4);
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-2) var(--space-4);
  border-bottom: 1px solid var(--border);
}
.titles { min-width: 0; display: flex; flex-direction: column; }
.titles strong { font-size: var(--text-strong); }
.titles span { color: var(--muted); font-size: var(--text-meta); }
.actions { flex: 1 1 auto; min-width: 0; display: flex; flex-wrap: wrap; align-items: center; justify-content: flex-end; gap: var(--space-2); }
.body { min-width: 0; }
.body.normal { padding: var(--space-4); }
.body.tight { padding: var(--space-2); }
.body.none { padding: 0; }
</style>
