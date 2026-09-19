<script setup lang="ts">
withDefaults(
  defineProps<{
    rows?: number;
    label?: string;
  }>(),
  { rows: 3, label: "Loading" }
);
</script>

<template>
  <div class="loading" role="status" :aria-label="label" data-testid="loading">
    <span v-for="row in rows" :key="row" class="loading__bar" />
    <span class="visually-hidden">{{ label }}</span>
  </div>
</template>

<style scoped>
.loading {
  display: grid;
  gap: 10px;
  padding: 16px 18px;
}

.loading__bar {
  height: 16px;
  border-radius: 5px;
  background: linear-gradient(90deg, var(--ns-line-soft), #f4f8f7, var(--ns-line-soft));
}

.loading__bar:nth-child(even) {
  width: 72%;
}
</style>
