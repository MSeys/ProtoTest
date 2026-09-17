<script setup lang="ts">
withDefaults(defineProps<{
  variant?: "primary" | "secondary" | "quiet" | "icon";
  type?: "button" | "submit";
  disabled?: boolean;
  label?: string;
}>(), {
  variant: "secondary",
  type: "button"
});
defineEmits<{ click: [event: MouseEvent] }>();
</script>

<template>
  <button :type="type" :disabled="disabled" :aria-label="label" :title="label" :class="variant" @click="$emit('click', $event)">
    <slot />
  </button>
</template>

<style scoped>
button {
  height: var(--control-height);
  padding: 0 var(--space-4);
  border: 1px solid var(--border-strong);
  border-radius: var(--radius-control);
  background: var(--surface-2);
  color: var(--text);
  font-size: var(--text-meta);
  white-space: nowrap;
  transition: border-color var(--motion-fast) var(--motion-ease), background var(--motion-fast) var(--motion-ease);
}
button:hover:not(:disabled) { border-color: var(--blueprint); }
button:disabled { opacity: .5; cursor: default; }
button.primary { border-color: var(--blueprint); background: var(--blueprint-soft); font-weight: var(--weight-bold); }
button.quiet { height: 26px; padding: 0 var(--space-3); border-color: transparent; background: transparent; color: var(--muted); }
button.quiet:hover:not(:disabled) { border-color: var(--border); color: var(--text); }
button.icon { width: var(--control-height); padding: 0; display: grid; place-items: center; font-size: var(--text-title); line-height: 1; }
</style>
