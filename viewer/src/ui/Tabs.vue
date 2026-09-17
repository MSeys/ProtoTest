<script setup lang="ts">
defineProps<{ items: { id: string; label: string; href?: string }[]; active: string; variant?: "underline" | "pill" }>();
const emit = defineEmits<{ select: [id: string] }>();
</script>

<template>
  <nav class="tabs" :class="variant ?? 'underline'" role="tablist">
    <template v-for="item in items" :key="item.id">
      <a v-if="item.href" :href="item.href" role="tab" :aria-selected="item.id === active" :class="{ active: item.id === active }">{{ item.label }}</a>
      <button v-else type="button" role="tab" :aria-selected="item.id === active" :class="{ active: item.id === active }" @click="emit('select', item.id)">{{ item.label }}</button>
    </template>
  </nav>
</template>

<style scoped>
/* Underline navigates between views; pill filters a list. One component, so they cannot drift apart. */
.tabs { display: flex; align-items: center; gap: 2px; overflow-x: auto; }
.tabs > a, .tabs > button {
  flex: none;
  border: 0;
  background: transparent;
  color: var(--muted);
  font-size: var(--text-meta);
  text-decoration: none;
  transition: color var(--motion-fast) var(--motion-ease), background var(--motion-fast) var(--motion-ease);
}
.underline > a, .underline > button {
  height: 32px;
  padding: 0 var(--space-4);
  display: inline-flex;
  align-items: center;
  border-bottom: 2px solid transparent;
  border-radius: var(--radius-chip) var(--radius-chip) 0 0;
}
.underline > a:hover, .underline > button:hover { background: var(--hover); color: var(--text); }
.underline > .active { border-bottom-color: var(--blueprint); color: var(--text); font-weight: var(--weight-bold); }
.pill { gap: var(--space-1); }
.pill > a, .pill > button {
  height: 24px;
  padding: 0 var(--space-4);
  display: inline-flex;
  align-items: center;
  border: 1px solid var(--border);
  border-radius: var(--radius-pill);
  font-size: var(--text-micro);
}
.pill > a:hover, .pill > button:hover { border-color: var(--border-strong); color: var(--text); }
.pill > .active { border-color: var(--blueprint); background: var(--blueprint-soft); color: var(--text); font-weight: var(--weight-bold); }
</style>
