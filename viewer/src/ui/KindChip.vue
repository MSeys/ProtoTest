<script setup lang="ts">
import type { KindLabel } from "../trace/format";

/** `quiet` drops the fill for lists, where a chip on every row would outweigh the names beside it. */
defineProps<{ type: KindLabel; quiet?: boolean }>();
</script>

<template>
  <!-- The colour comes from the shared token file: --type-<id> falls back to the action family, so an
       integration's own span kinds get a chip without the viewer knowing about them. -->
  <span class="chip" :class="{ quiet }" :style="{ '--node-color': `var(--type-${type.id}, var(--type-custom))` }">{{ type.label }}</span>
</template>

<style scoped>
/* A label, not data: the UI face, small and semibold, tinted by its family. */
.chip {
  padding: 0 var(--space-2);
  border-radius: var(--radius-chip);
  background: color-mix(in srgb, var(--node-color) 14%, transparent);
  color: color-mix(in srgb, var(--node-color) 78%, var(--text));
  font-size: var(--text-micro);
  font-weight: var(--weight-semibold);
  line-height: 1.8;
  white-space: nowrap;
}
.chip.quiet { padding: 0; background: transparent; color: color-mix(in srgb, var(--node-color) 70%, var(--muted)); font-weight: var(--weight-medium); }
</style>
