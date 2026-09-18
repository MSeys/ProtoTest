<script setup lang="ts">
import type { Outcome } from "../trace/model";
import { outcomeLabel, tone } from "../trace/format";

defineProps<{ outcome: Outcome; detail?: string }>();
</script>

<template>
  <span class="pill" :class="tone(outcome)"><i class="status" :class="tone(outcome)" />{{ outcomeLabel(outcome) }}<em v-if="detail">{{ detail }}</em></span>
</template>

<style scoped>
.pill { display: inline-flex; align-items: center; gap: var(--space-2); font-size: var(--text-meta); font-weight: var(--weight-semibold); white-space: nowrap; }
.pill em { color: var(--muted); font-size: var(--text-micro); font-style: normal; font-weight: var(--weight-regular); }
.pill.success { color: var(--success); }
.pill.warning { color: var(--warning); }
.pill.danger { color: var(--danger); }
.pill.neutral { color: var(--muted); }
</style>
