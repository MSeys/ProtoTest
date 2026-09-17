<script setup lang="ts">
import { phaseOrder } from "../model/trace-schema";

/** The phases a test runs through, named, in order, in the words the docs use. */
const phases = phaseOrder.filter(phase => phase !== "Run");
</script>

<template>
  <ul class="key">
    <li v-for="phase in phases" :key="phase">
      <i :style="{ background: `var(--phase-${phase.toLocaleLowerCase()})` }" />{{ phase }}
    </li>
  </ul>
</template>

<style scoped>
.key { margin: 0; padding: 0; display: flex; flex-wrap: wrap; align-items: center; gap: var(--space-2) var(--space-4); list-style: none; }
.key li { display: inline-flex; align-items: center; gap: var(--space-2); color: var(--muted); font-size: var(--text-micro); }
.key i { width: 12px; height: 4px; border-radius: var(--radius-hairline); }
</style>
