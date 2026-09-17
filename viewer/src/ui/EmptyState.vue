<script setup lang="ts">
defineProps<{ message: string }>();
</script>

<template>
  <div class="empty">
    <p>{{ message }}</p>
    <div v-if="$slots.default" class="action"><slot /></div>
  </div>
</template>

<style scoped>
.empty { padding: var(--space-6) var(--space-4); display: grid; justify-items: center; gap: var(--space-4); text-align: center; }
.empty p { color: var(--muted); font-size: var(--text-body); }
.action { display: flex; gap: var(--space-2); }
</style>
