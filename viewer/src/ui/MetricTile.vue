<script setup lang="ts">
withDefaults(defineProps<{ label: string; value: string | number; tone?: "neutral" | "success" | "warning" | "danger" }>(), { tone: "neutral" });
</script>

<template>
  <div class="tile" :class="tone">
    <span>{{ label }}</span>
    <strong>{{ value }}</strong>
  </div>
</template>

<style scoped>
.tile {
  min-width: 84px;
  min-height: 52px;
  padding: var(--space-2) var(--space-4);
  display: flex;
  flex-direction: column;
  justify-content: center;
  gap: 1px;
  border: 1px solid var(--border);
  border-radius: var(--radius-control);
  background: var(--surface);
}
.tile span { color: var(--muted); font-size: var(--text-meta); }
.tile strong { font: var(--weight-bold) var(--text-title) var(--font-mono); }
.tile.success strong { color: var(--success); }
.tile.warning strong { color: var(--warning); }
.tile.danger strong { color: var(--danger); }
</style>
