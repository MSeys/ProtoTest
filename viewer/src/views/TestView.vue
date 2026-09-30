<script setup lang="ts">
import { computed } from "vue";
import type { Artifact, Item, Span, TestTrace } from "../trace/model";
import { formatDuration, pad, testCodeName, testGroup, testTitle } from "../trace/format";
import type { TestView as TestViewId } from "../router";
import StepsView from "./StepsView.vue";
import StateView from "./StateView.vue";
import TimelineView from "./TimelineView.vue";
import Tabs from "../ui/Tabs.vue";
import VerdictBar from "../ui/VerdictBar.vue";
import PhaseBand from "../ui/PhaseBand.vue";
import OutcomePill from "../ui/OutcomePill.vue";
import EvidenceView from "./EvidenceView.vue";

/*
 * One test: who it is and how it ended, why it did not pass, and the views onto what it did. The views
 * share one selection with the details beside them, so switching view keeps the reader's place.
 */
const props = defineProps<{
  test: TestTrace;
  view: TestViewId;
  tabs: { id: string; label: string; href: string }[];
  selectedSpan?: Span;
  selectedItem?: Item;
  /** The neighbours in the list's current filter and order, as links. */
  previous?: { test: TestTrace; href: string };
  next?: { test: TestTrace; href: string };
}>();
const emit = defineEmits<{ selectSpan: [span: Span | undefined]; selectItem: [item: Item]; artifact: [artifact: Artifact]; tab: [id: string] }>();

const itemSelection = computed(() => props.selectedItem ? { kind: props.selectedItem.kind, id: props.selectedItem.id } : undefined);

/* Every outcome short of pass and skip says why, even when no operation failed. */
const explains = computed(() => props.test.outcome !== "succeeded" && props.test.outcome !== "skipped");
</script>

<template>
  <div class="test">
    <header class="test-head">
      <nav class="stepper" aria-label="Step through the tests">
        <a v-if="previous" :href="previous.href" class="step" :title="`${pad(previous.test.number)} ${testTitle(previous.test)}`" aria-label="Previous test">
          <svg viewBox="0 0 10 10" width="10" height="10" aria-hidden="true"><path d="M6.4 2 3.4 5 6.4 8" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" /></svg>
        </a>
        <span v-else class="step" aria-hidden="true" />
        <b>{{ pad(test.number) }}</b>
        <a v-if="next" :href="next.href" class="step" :title="`${pad(next.test.number)} ${testTitle(next.test)}`" aria-label="Next test">
          <svg viewBox="0 0 10 10" width="10" height="10" aria-hidden="true"><path d="M3.6 2 6.6 5 3.6 8" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" /></svg>
        </a>
        <span v-else class="step" aria-hidden="true" />
      </nav>
      <div class="test-title">
        <h1>{{ testTitle(test) }}</h1>
        <p><span>{{ testGroup(test) }}</span><code>{{ testCodeName(test) }}</code></p>
      </div>
      <OutcomePill :outcome="test.outcome" :detail="formatDuration(test.duration)" />
    </header>

    <VerdictBar v-if="explains" :test="test" @select="emit('selectSpan', $event)" />
    <PhaseBand :test="test" />

    <div class="views">
      <Tabs :items="tabs" :active="view" variant="underline" label="Views of this test" panel="test-view" @select="emit('tab', $event)" />
    </div>
    <div id="test-view" role="tabpanel" :aria-labelledby="`test-view-tab-${view}`">
      <StepsView v-if="view === 'steps'" :test="test" :selected="selectedSpan?.id" @select="emit('selectSpan', $event)" />
      <StateView v-else-if="view === 'state'" :test="test" :selected="itemSelection" :selected-span="selectedSpan?.id"
                 @select-item="emit('selectItem', $event)" @select-span="emit('selectSpan', $event)" />
      <EvidenceView v-else-if="view === 'evidence'" :test="test" @select="emit('selectSpan', $event)" @artifact="emit('artifact', $event)" />
      <TimelineView v-else :test="test" :selected="selectedSpan?.id" @select="emit('selectSpan', $event)" />
    </div>
  </div>
</template>

<style scoped>
/* Sticky heads inside a view stop under the view tabs, which stay pinned at the top (32px tabs and a hairline). */
.test { --sticky-offset: 33px; display: grid; gap: var(--space-3); container-type: inline-size; }
.test-head { padding: var(--space-1) var(--space-1) 0; display: grid; grid-template-columns: auto minmax(0, 1fr) auto; align-items: start; gap: var(--space-3); }
/* The number is the rail's number; the arrows walk the rail's list in its current filter and order. */
.stepper { padding-top: 2px; display: flex; align-items: center; gap: 2px; }
.stepper b { min-width: 3ch; color: var(--dim); font: var(--weight-semibold) var(--text-body) var(--font-mono); text-align: center; }
.step { width: 22px; height: 22px; display: grid; place-items: center; border: 1px solid var(--border); border-radius: var(--radius-chip); color: var(--muted); }
a.step:hover { border-color: var(--blueprint); color: var(--text); }
span.step { visibility: hidden; }
.test-title { min-width: 0; display: grid; gap: 2px; }
.test-title h1 { font-size: var(--text-heading); letter-spacing: var(--tracking-display); line-height: var(--leading-tight); overflow-wrap: anywhere; }
.test-title p { display: flex; flex-wrap: wrap; gap: var(--space-1) var(--space-3); color: var(--muted); font-size: var(--text-meta); }
.test-title code { overflow-wrap: anywhere; color: var(--dim); font-family: var(--font-mono); }
.test-head :deep(.pill) { padding-top: var(--space-1); }
/* The view tabs stay in reach while the view scrolls under them. */
.views { position: sticky; top: 0; z-index: 3; border-bottom: 1px solid var(--border); background: var(--bg); }

/* Narrow: the outcome keeps its own row under the title instead of squeezing it. */
@container (max-width: 560px) {
  .test-head { grid-template-columns: auto minmax(0, 1fr); }
  .test-head :deep(.pill) { grid-column: 2; padding-top: 0; }
}
</style>
