<script setup lang="ts">
import { computed } from "vue";
import type { TestTrace } from "../trace/model";
import { formatOffset } from "../trace/format";
import { looseEvents, type LooseObservation } from "./looseEvents";
import Panel from "./Panel.vue";
import JsonView from "../inspector/JsonView.vue";
import SectionView from "../inspector/SectionView.vue";

const props = defineProps<{ test: TestTrace }>();
const events = computed(() => looseEvents(props.test));

function offset(at: number): string {
  return formatOffset(at - props.test.start);
}

function hasMetadata(observation: LooseObservation): boolean {
  return Object.keys(observation.metadata ?? {}).length > 0;
}
</script>

<template>
  <Panel v-if="events.length" title="Outside any operation"
         subtitle="Moments and observations the trace recorded with no operation above them." pad="none">
    <div class="loose">
      <div v-for="(event, index) in events" :key="index" class="row">
        <span class="offset">{{ offset(event.at) }}</span>
        <div v-if="event.type === 'moment'" class="what">
          <p><span>{{ event.moment.name }}</span><small>{{ event.moment.kind }}</small></p>
          <SectionView v-for="section in event.moment.sections" :key="section.label" :section="section" />
        </div>
        <div v-else class="what">
          <p><strong>Observed</strong> {{ event.observation.kind }}
            <code v-if="event.observation.identifier">{{ event.observation.identifier }}</code>
            <span class="muted">on {{ event.observation.target }}</span></p>
          <JsonView v-if="event.observation.data" :value="event.observation.data" :label="event.observation.identifier ?? event.observation.kind" :open-depth="0" />
          <JsonView v-if="hasMetadata(event.observation)" :value="event.observation.metadata" label="Metadata" :open-depth="0" />
        </div>
      </div>
    </div>
  </Panel>
</template>

<style scoped>
.loose { display: grid; }
.row {
  padding: var(--space-2) var(--space-4);
  display: grid;
  grid-template-columns: 64px minmax(0, 1fr);
  gap: var(--space-3);
}
.row + .row { border-top: 1px solid var(--border); }
.offset { color: var(--muted); font-size: var(--text-micro); font-variant-numeric: tabular-nums; }
.what { min-width: 0; display: grid; gap: var(--space-2); align-content: start; }
.what p { font-size: var(--text-meta); overflow-wrap: anywhere; }
.what small, .muted { color: var(--muted); font-size: var(--text-micro); }
.what code { color: var(--dim); font-family: var(--font-mono); font-size: var(--text-micro); }
</style>
