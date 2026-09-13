<script setup lang="ts">
import { computed } from "vue";
import type { TestTrace, TraceArtifact, TraceRun } from "../trace-schema";
import { formatDate, formatDuration, milliseconds, pad, phaseOrder, phaseSummary, runDuration, testDisplayName, tone } from "../trace-utils";

const props = defineProps<{ run: TraceRun; fileName: string }>();
const emit = defineEmits<{ select: [test: TestTrace]; artifact: [artifact: TraceArtifact] }>();
const failed = computed(() => props.run.tests.filter(test => test.outcome === "Failed").length);
const passed = computed(() => props.run.tests.filter(test => test.outcome === "Succeeded").length);
const partial = computed(() => props.run.tests.filter(test => test.outcome === "Partial").length);
const entryCount = computed(() => props.run.tests.reduce((sum, test) => sum + test.entries.length, 0));
const artifactCount = computed(() => (props.run.artifacts?.length ?? 0) + props.run.tests.reduce((sum, test) => sum + (test.artifacts?.length ?? 0), 0));
const bounds = computed(() => {
  if (!props.run.tests.length) return { start: 0, duration: 1 };
  const start = Math.min(...props.run.tests.map(test => Date.parse(test.startedAtUtc)));
  const end = Math.max(...props.run.tests.map(test => Date.parse(test.startedAtUtc) + milliseconds(test.duration)));
  return { start, duration: Math.max(end - start, 1) };
});
const phaseLegend = ["Setup", "Execution", "Rollback", "Teardown"];
const timeline = computed(() => {
  const laneEnds: number[] = [];
  const items = [...props.run.tests]
    .sort((left, right) => Date.parse(left.startedAtUtc) - Date.parse(right.startedAtUtc))
    .map(test => {
      const started = Date.parse(test.startedAtUtc);
      const ended = started + milliseconds(test.duration);
      let lane = laneEnds.findIndex(laneEnd => laneEnd <= started);
      if (lane < 0) { lane = laneEnds.length; laneEnds.push(ended); } else laneEnds[lane] = ended;
      return {
        test,
        lane,
        left: ((started - bounds.value.start) / bounds.value.duration) * 100,
        width: Math.max((milliseconds(test.duration) / bounds.value.duration) * 100, .45),
        segments: phaseOrder
          .filter(phase => phase !== "Run")
          .map(phase => {
            const entries = test.entries.filter(entry => entry.phase === phase);
            if (!entries.length) return undefined;
            const phaseStart = Math.min(...entries.map(entry => Date.parse(entry.timestampUtc)));
            const phaseEnd = Math.max(...entries.map(entry => Date.parse(entry.timestampUtc) + milliseconds(entry.duration)));
            return {
              phase,
              outcome: phaseSummary(test, phase).outcome,
              left: Math.max(0, ((phaseStart - started) / Math.max(milliseconds(test.duration), 1)) * 100),
              width: Math.max(.5, ((phaseEnd - phaseStart) / Math.max(milliseconds(test.duration), 1)) * 100)
            };
          })
          .filter(segment => segment !== undefined)
      };
    });
  return { items, lanes: Math.max(laneEnds.length, 1) };
});
</script>

<template>
  <div class="run-heading">
    <div><span class="eyebrow">Test run</span><h1>{{ fileName }}</h1><p>{{ formatDate(run.startedAtUtc) }} · RUN {{ run.runId.slice(0, 8).toUpperCase() }}</p><div v-if="run.environment" class="environment-line"><span>{{ run.environment.runtime }}</span><span>{{ run.environment.os }}</span><span>{{ run.environment.processArchitecture }}</span></div></div>
    <div class="metrics">
      <div class="metric"><span>Tests</span><strong>{{ run.tests.length }}</strong></div>
      <div class="metric success"><span>Passed</span><strong>{{ passed }}</strong></div>
      <div v-if="partial" class="metric warning"><span>Partial</span><strong>{{ partial }}</strong></div>
      <div class="metric" :class="{ danger: failed }"><span>Failed</span><strong>{{ failed }}</strong></div>
      <div class="metric"><span>Trace entries</span><strong>{{ entryCount }}</strong></div>
      <div v-if="artifactCount" class="metric"><span>Artifacts</span><strong>{{artifactCount}}</strong></div>
      <div class="metric"><span>Duration</span><strong>{{ formatDuration(runDuration(run.tests)) }}</strong></div>
    </div>
  </div>
  <div class="timeline-panel">
    <div class="panel-heading"><strong>Run timeline</strong><div class="run-phase-key"><em>Number: status · bar: phase</em><span v-for="phase in phaseLegend" :key="phase" :class="`phase-${phase.toLocaleLowerCase()}`"><i />{{ phase }}</span><b>SPAN / {{ formatDuration(bounds.duration) }}</b></div></div>
    <div class="run-scale"><span v-for="tick in 6" :key="tick">{{ formatDuration(bounds.duration * (tick - 1) / 5) }}</span></div>
    <div class="run-timeline" :style="{ height: `${timeline.lanes * 25 + 16}px` }">
      <button v-for="item in timeline.items" :key="item.test.testId" type="button"
              class="timeline-bar" :class="tone(item.test.outcome)" :data-index="pad(run.tests.indexOf(item.test) + 1)"
              :title="`${testDisplayName(item.test)} · ${formatDuration(milliseconds(item.test.duration))}`"
              :style="{ left: `${item.left}%`, width: `${item.width}%`, top: `${item.lane * 25 + 12}px` }"
              @click="emit('select', item.test)">
        <i v-for="segment in item.segments" :key="segment.phase" class="run-phase-segment" :class="[`phase-${segment.phase.toLocaleLowerCase()}`, tone(segment.outcome)]" :style="{ left: `${segment.left}%`, width: `${segment.width}%` }" />
      </button>
    </div>
  </div>
  <section v-if="run.artifacts?.length" class="run-output-panel">
    <div class="panel-heading"><div><strong>Run outputs</strong><span>Generated by report sinks and bundled inside this trace</span></div><span>{{ run.artifacts.length }}</span></div>
    <div class="run-output-list"><button v-for="artifact in run.artifacts" :key="artifact.id" type="button" @click="emit('artifact', artifact)"><span class="category-glyph">{{ artifact.mediaType.includes('html') ? 'HT' : artifact.mediaType.includes('json') ? 'JS' : 'AR' }}</span><span><strong>{{ artifact.name }}</strong><small>{{ artifact.description || artifact.mediaType }}</small></span><b>Open →</b></button></div>
  </section>
</template>
