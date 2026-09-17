<script setup lang="ts">
import type { TraceOutcome } from "../model/trace-schema";
import { tone } from "../model/trace-format";

defineProps<{ outcome: TraceOutcome; detail?: string }>();
</script>

<template>
  <span class="pill" :class="tone(outcome)"><i class="status" :class="tone(outcome)" />{{ outcome }}<em v-if="detail">{{ detail }}</em></span>
</template>

<style scoped>
.pill { display: inline-flex; align-items: center; gap: var(--space-2); font-size: var(--text-meta); white-space: nowrap; }
.pill em { color: var(--muted); font: var(--text-micro) var(--font-mono); font-style: normal; }
.pill.success { color: var(--success); }
.pill.warning { color: var(--warning); }
.pill.danger { color: var(--danger); }
.pill.neutral { color: var(--muted); }
</style>
