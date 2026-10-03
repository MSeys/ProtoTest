<script setup lang="ts">
import { computed } from "vue";
import { groupCapabilities, groupResources } from "../trace/analysis";
import type { ChangeSource, Item, Visibility } from "../trace/model";
import Panel from "./Panel.vue";

const props = withDefaults(defineProps<{ visibility: Visibility; resources?: Item[] }>(), { resources: () => [] });

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

/* A clock on three hosts is one capability three times: it reads once, with the hosts behind the title. */
const capabilities = computed(() => groupCapabilities(props.visibility.capabilities));
function capabilityTitle(group: { instances: string[]; sources: string[] }): string | undefined {
  return [group.instances.join(", "), group.sources.join(", ")].filter(Boolean).join(" · ") || undefined;
}

/* Resources read per kind, short names as chips; the id and its state stay behind the title for the exact key. */
const resourceGroups = computed(() => groupResources(props.resources));
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
        <dd v-if="capabilities.length" class="chips">
          <span v-for="capability in capabilities" :key="capability.name" class="capability"
                :title="capabilityTitle(capability)">{{ capability.name }}<span v-if="capability.count > 1" class="count"> ×{{ capability.count }}</span></span>
        </dd>
        <dd v-else class="absent">None recorded</dd>
      </div>
      <div v-if="visibility.backends.length">
        <dt>Backends</dt>
        <dd>{{ visibility.backends.join(", ") }}</dd>
      </div>
      <div v-if="resourceGroups.length" class="resources">
        <dt>Resources</dt>
        <dd>
          <div v-for="group in resourceGroups" :key="group.label" class="group">
            <span class="kind">{{ group.label }}</span>
            <span class="chips">
              <span v-for="entry in group.entries" :key="entry.text" class="resource" :title="entry.title">{{ entry.text }}</span>
            </span>
          </div>
        </dd>
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
.capability .count { color: var(--dim); }
/* Resources can run long, so they take the full width: one row per kind, its resources as quiet chips. */
dl > div.resources { flex-basis: 100%; align-items: flex-start; }
.resources > dt { padding-top: var(--space-1); }
.resources > dd { display: grid; grid-template-columns: max-content minmax(0, 1fr); gap: var(--space-1) var(--space-3); align-items: baseline; }
.resources .group { display: contents; }
.resources .kind { color: var(--dim); font-size: var(--text-micro); font-weight: var(--weight-regular); }
.resource { padding: 0 var(--space-2); border-radius: var(--radius-chip); background: var(--surface-2, color-mix(in srgb, var(--border) 45%, transparent)); line-height: 1.8; font-weight: var(--weight-regular); }
/* Present is solid, absent is the dashed outline of the same chip: the gap keeps its place. */
.source { padding: 0 var(--space-2); display: inline-flex; align-items: center; gap: var(--space-1); border: 1px solid var(--blueprint); border-radius: var(--radius-chip); background: var(--blueprint-soft); line-height: 1.8; }
.source i { width: 6px; height: 6px; border-radius: 50%; background: var(--blueprint); }
.source.absent { border-style: dashed; border-color: var(--border-strong); background: transparent; color: var(--dim); }
.source.absent i { background: transparent; box-shadow: inset 0 0 0 1px var(--dim); }
dd.absent { color: var(--dim); font-weight: var(--weight-regular); }
.visually-hidden { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); }

</style>
