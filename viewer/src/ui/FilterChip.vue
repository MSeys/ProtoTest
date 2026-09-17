<script setup lang="ts">
withDefaults(defineProps<{
  label: string;
  count?: number;
  active?: boolean;
  /** Colours the chip when it is active, so a "Failed 1" filter reads as failure before it is read. */
  tone?: "neutral" | "success" | "warning" | "danger";
}>(), { tone: "neutral" });
defineEmits<{ select: [] }>();
</script>

<template>
  <button type="button" class="chip" :class="[tone, { active }]" :aria-pressed="active" @click="$emit('select')">
    {{ label }}<b v-if="count !== undefined">{{ count }}</b>
  </button>
</template>

<style scoped>
.chip {
  height: 24px;
  padding: 0 var(--space-3);
  display: inline-flex;
  align-items: center;
  gap: var(--space-2);
  border: 1px solid var(--border);
  border-radius: var(--radius-pill);
  background: transparent;
  color: var(--muted);
  font-size: var(--text-micro);
  white-space: nowrap;
  transition: border-color var(--motion-fast) var(--motion-ease), color var(--motion-fast) var(--motion-ease);
}
.chip b { color: var(--dim); font: var(--weight-semibold) var(--text-micro) var(--font-mono); }
.chip:hover { border-color: var(--border-strong); color: var(--text); }
.chip.active { border-color: var(--blueprint); background: var(--blueprint-soft); color: var(--text); font-weight: var(--weight-bold); }
.chip.active b { color: var(--text); }
.chip.success.active { border-color: var(--success-line); background: var(--success-soft); }
.chip.warning.active { border-color: var(--warning-line); background: var(--warning-soft); }
.chip.danger.active { border-color: var(--danger-line); background: var(--danger-soft); }
</style>
