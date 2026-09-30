<script setup lang="ts">
import { computed } from "vue";
import type { TestTrace } from "../trace/model";
import { outcomeCounts, testFilter, type OutcomeFilter } from "./testFilter";
import FilterChip from "./FilterChip.vue";

/*
 * The outcome filter every test list shares. It is one state, so choosing "Needs attention" in the rail
 * filters the run's list and the step between tests too. An outcome the run never had is not offered.
 */
const props = defineProps<{ tests: TestTrace[] }>();

const counts = computed(() => outcomeCounts(props.tests));
const filters = computed(() => ([
  ["all", "All", "neutral"],
  ["attention", "Needs attention", "danger"],
  ["passed", "Passed", "success"],
  ["skipped", "Skipped", "neutral"]
] as [OutcomeFilter, string, "neutral" | "danger" | "success"][])
  .filter(([id]) => id === "all" || id === testFilter.outcome || counts.value[id] > 0));
</script>

<template>
  <div class="filters" role="group" aria-label="Show tests">
    <FilterChip v-for="[id, label, chipTone] in filters" :key="id" :label="label" :count="counts[id]"
                :tone="chipTone" :active="testFilter.outcome === id" @select="testFilter.outcome = id" />
  </div>
</template>

<style scoped>
.filters { display: flex; flex-wrap: wrap; gap: var(--space-1); }
</style>
