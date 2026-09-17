<script setup lang="ts">
defineProps<{ type: { id: string; label: string } }>();
</script>

<template>
  <!-- The colour comes from the shared token file: --type-<id> falls back to the framework family,
       so an integration's own entry kinds get a chip without the viewer knowing about them. -->
  <span class="chip" :style="{ '--node-color': `var(--type-${type.id}, var(--type-custom))` }">{{ type.label }}</span>
</template>

<style scoped>
.chip {
  padding: 0 var(--space-2);
  border-radius: var(--radius-chip);
  background: color-mix(in srgb, var(--node-color) 16%, transparent);
  color: color-mix(in srgb, var(--node-color) 82%, var(--text));
  font: var(--weight-semibold) var(--text-micro)/1.8 var(--font-mono);
  letter-spacing: .03em;
  white-space: nowrap;
}
</style>
