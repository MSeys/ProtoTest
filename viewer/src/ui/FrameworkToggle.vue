<script setup lang="ts">
import { frameworkMode, type FrameworkMode } from "./frameworkMode";

/* One quiet switch for the framework's own operations, shared by every view that lists operations. */
const options: [FrameworkMode, string, string][] = [
  ["show", "Show", "Show framework operations like any other"],
  ["dim", "Dim", "Show framework operations, stepped back"],
  ["hide", "Hide", "Leave out framework operations that did nothing wrong"]
];
</script>

<template>
  <div class="toggle" role="radiogroup" aria-label="Framework operations">
    <span class="label">Framework</span>
    <button v-for="[mode, label, hint] in options" :key="mode" type="button" role="radio" :aria-checked="frameworkMode === mode"
            :title="hint" @click="frameworkMode = mode">{{ label }}</button>
  </div>
</template>

<style scoped>
.toggle { display: inline-flex; align-items: center; gap: var(--space-1); color: var(--dim); font-size: var(--text-meta); white-space: nowrap; }
.label { margin-right: var(--space-1); }
button { height: 22px; padding: 0 var(--space-2); border: 0; border-radius: var(--radius-chip); background: transparent; color: var(--muted); font-size: var(--text-meta); }
button:hover { color: var(--text); }
button[aria-checked="true"] { background: var(--surface-2); color: var(--text); font-weight: var(--weight-semibold); }
</style>
