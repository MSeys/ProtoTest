<script setup lang="ts">
/*
 * The one frame for a recorded document in the details: source, a validated shape, a JSON body, a code block.
 * The head names it, says how big it is and holds its tools; on a narrow panel the tools drop under the name
 * instead of squeezing it.
 */
defineProps<{ title?: string; meta?: string; mono?: boolean; hint?: string }>();
</script>

<template>
  <section class="card">
    <header>
      <span class="name">
        <strong v-if="title" :class="{ mono }" :title="hint ?? title">{{ title }}</strong>
        <small v-if="meta" :title="meta">{{ meta }}</small>
      </span>
      <span v-if="$slots.tools" class="tools"><slot name="tools" /></span>
    </header>
    <slot />
  </section>
</template>

<style scoped>
.card { min-width: 0; border: 1px solid var(--border); border-radius: var(--radius-control); background: var(--surface-sunken); overflow: clip; }
header {
  min-height: var(--row-height);
  padding: var(--space-1) var(--space-2) var(--space-1) var(--space-3);
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0 var(--space-2);
  border-bottom: 1px solid var(--border);
  background: var(--surface-2);
}
.name { min-width: 0; flex: 1 1 12em; display: flex; align-items: baseline; gap: var(--space-2); }
strong { flex: none; max-width: 100%; overflow: hidden; font-size: var(--text-meta); font-weight: var(--weight-bold); text-overflow: ellipsis; white-space: nowrap; }
strong.mono { font-family: var(--font-mono); }
small { min-width: 0; overflow: hidden; color: var(--muted); font-size: var(--text-micro); text-overflow: ellipsis; white-space: nowrap; }
.tools { margin-left: auto; display: flex; flex: none; align-items: center; gap: var(--space-1); }
.tools :slotted(button) {
  height: 20px;
  padding: 0 var(--space-2);
  border: 1px solid transparent;
  border-radius: var(--radius-chip);
  background: transparent;
  color: var(--muted);
  font-size: var(--text-micro);
  white-space: nowrap;
  transition: color var(--motion-fast) var(--motion-ease), border-color var(--motion-fast) var(--motion-ease);
}
.tools :slotted(button:hover) { border-color: var(--border); color: var(--text); }
.tools :slotted(small) { color: var(--muted); font: var(--text-micro) var(--font-mono); white-space: nowrap; }
</style>
