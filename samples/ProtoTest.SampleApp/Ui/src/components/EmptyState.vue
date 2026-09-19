<script setup lang="ts">
defineProps<{
  title: string;
  message: string;
  testid?: string;
}>();
</script>

<template>
  <div class="empty" :data-testid="testid">
    <h2 class="empty__title">{{ title }}</h2>
    <p class="muted">{{ message }}</p>
    <div v-if="$slots.action" class="empty__action">
      <slot name="action" />
    </div>
  </div>
</template>

<style scoped>
.empty {
  display: grid;
  justify-items: center;
  gap: 6px;
  padding: 40px 20px;
  text-align: center;
}

.empty__action {
  margin-top: 10px;
}
</style>
