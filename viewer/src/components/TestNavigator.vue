<script setup lang="ts">
import { computed, ref } from "vue";
import type { TestTrace } from "../trace-schema";
import { formatDuration, milliseconds, pad, testDisplayName, testGroupName, testGroupNamespace, tone } from "../trace-utils";

const props = defineProps<{ tests: TestTrace[]; selected?: TestTrace | null }>();
const emit = defineEmits<{ select: [test: TestTrace] }>();
const query = ref("");
const filtered = computed(() => {
  const value = query.value.trim().toLocaleLowerCase();
  return props.tests.filter(test => !value || `${test.name} ${test.className ?? ""}`.toLocaleLowerCase().includes(value));
});
const groups = computed(() => {
  const result = new Map<string, { name: string; namespace: string; tests: TestTrace[] }>();
  for (const test of filtered.value) {
    const key = test.className ?? "Other tests";
    const group = result.get(key) ?? { name: testGroupName(test), namespace: testGroupNamespace(test), tests: [] };
    group.tests.push(test);
    result.set(key, group);
  }
  return [...result.values()];
});
function testIndex(test: TestTrace) { return props.tests.findIndex(item => item.testId === test.testId) + 1; }
</script>

<template>
  <aside class="test-panel">
    <div class="panel-heading"><strong>Tests</strong><span>{{ pad(tests.length) }}</span></div>
    <label class="search"><span aria-hidden="true">⌕</span><input v-model="query" type="search" placeholder="Filter tests" aria-label="Filter tests"></label>
    <div class="test-list">
      <section v-for="group in groups" :key="group.name + group.namespace" class="test-group">
        <header :title="group.namespace"><strong>{{ group.name }}</strong><span>{{ pad(group.tests.length) }}</span></header>
        <button v-for="test in group.tests" :key="test.testId" type="button" class="test-item" :title="test.name"
                :class="{ selected: selected?.testId === test.testId }" @click="emit('select', test)">
          <span class="test-index">{{ pad(testIndex(test)) }}</span><span class="test-name">{{ testDisplayName(test) }}</span>
          <span class="test-meta"><i class="status" :class="tone(test.outcome)" />{{ test.outcome }} · {{ formatDuration(milliseconds(test.duration)) }}</span>
        </button>
      </section>
    </div>
  </aside>
</template>
