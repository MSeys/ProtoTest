<script setup lang="ts">
import { computed } from "vue";
import type { ChangeSource, Visibility } from "../trace/model";
import Panel from "./Panel.vue";

const props = defineProps<{ visibility: Visibility }>();

const hosting = computed(() => ({
  "in-process": { label: "In-process", detail: "The application ran inside the test host, so its own work could be traced." },
  remote: { label: "Remote", detail: "Tests called an application running elsewhere; only what crossed the wire is visible." },
  unknown: { label: "Not recorded", detail: "Nothing in this trace says where the application ran." }
})[props.visibility.hosting]);

/*
 * The three depths a value can come from. Each is stated, present or not: a missing source is the most
 * important thing this strip says, because it is the gap a reader would otherwise mistake for "nothing happened".
 */
const sources: { id: ChangeSource; label: string; detail: string }[] = [
  { id: "testside", label: "Test side", detail: "Values the tests recorded themselves." },
  { id: "observed", label: "Observed", detail: "Values read back from responses and stores." },
  { id: "applicationside", label: "Application", detail: "Values the application reported through its own instrumentation." }
];
const seen = computed(() => new Set(props.visibility.sources));
</script>

<template>
  <Panel title="What this run could see" subtitle="Where the application ran, what was composed, and how deep the trace reached." pad="normal">
    <dl>
      <div>
        <dt>Application</dt>
        <dd :title="hosting.detail">{{ hosting.label }}</dd>
      </div>
      <div>
        <dt>Capabilities</dt>
        <dd v-if="visibility.capabilities.length" class="chips">
          <span v-for="capability in visibility.capabilities" :key="capability.id" class="capability"
                :title="capability.state['capability.source'] ?? undefined">{{ capability.name }}</span>
        </dd>
        <dd v-else class="absent">None recorded</dd>
      </div>
      <div>
        <dt>Values from</dt>
        <dd class="chips">
          <span v-for="source in sources" :key="source.id" class="source" :class="{ absent: !seen.has(source.id) }"
                :title="seen.has(source.id) ? source.detail : `Not in this trace. ${source.detail}`">
            <i aria-hidden="true" />{{ source.label }}<span class="visually-hidden">{{ seen.has(source.id) ? "" : " (not visible)" }}</span>
          </span>
        </dd>
      </div>
    </dl>
  </Panel>
</template>

<style scoped>
dl { margin: 0; display: flex; flex-wrap: wrap; gap: var(--space-2) var(--space-6); }
dl > div { display: flex; align-items: center; gap: var(--space-3); }
dt { color: var(--dim); font-size: var(--text-micro); }
dd { margin: 0; font-size: var(--text-meta); font-weight: var(--weight-semibold); }
.chips { display: flex; flex-wrap: wrap; gap: var(--space-1); font-weight: var(--weight-regular); }
.capability { padding: 0 var(--space-2); border: 1px solid var(--border); border-radius: var(--radius-chip); line-height: 1.8; }
/* Present is solid, absent is the dashed outline of the same chip: the gap keeps its place. */
.source { padding: 0 var(--space-2); display: inline-flex; align-items: center; gap: var(--space-1); border: 1px solid var(--blueprint); border-radius: var(--radius-chip); background: var(--blueprint-soft); line-height: 1.8; }
.source i { width: 6px; height: 6px; border-radius: 50%; background: var(--blueprint); }
.source.absent { border-style: dashed; border-color: var(--border-strong); background: transparent; color: var(--dim); }
.source.absent i { background: transparent; box-shadow: inset 0 0 0 1px var(--dim); }
dd.absent { color: var(--dim); font-weight: var(--weight-regular); }
.visually-hidden { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); }

</style>
