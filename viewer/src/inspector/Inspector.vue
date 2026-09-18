<script setup lang="ts">
import { computed, ref } from "vue";
import type { Artifact, Item, Span, TestTrace } from "../trace/model";
import { formatDuration, formatOffset, itemKindLabel, itemTitle, kindLabel } from "../trace/format";
import AppButton from "../ui/AppButton.vue";
import KindChip from "../ui/KindChip.vue";
import OutcomePill from "../ui/OutcomePill.vue";
import SpanInspector from "./SpanInspector.vue";
import ItemInspector from "./ItemInspector.vue";

/*
 * The inspector's head says what is selected and stays put: where it ran, what it is, how it went. Only the
 * detail under it scrolls. An operation and a tracked item share the head's shape, so the panel reads the
 * same whichever the reader walked to.
 */
const props = defineProps<{ test: TestTrace; span?: Span; item?: Item }>();
const emit = defineEmits<{ select: [span: Span]; item: [item: Item]; artifact: [artifact: Artifact]; close: [] }>();

const path = computed(() => {
  const trail: Span[] = [];
  for (let current = props.span?.parent ?? null; current; current = current.parent) trail.unshift(current);
  return trail;
});
/** The address already holds the test, the view and the selection; copying it shares exactly this place. */
const copied = ref(false);
async function copyLink() {
  try {
    await navigator.clipboard.writeText(location.href);
    copied.value = true;
    setTimeout(() => { copied.value = false; }, 1400);
  } catch { /* the clipboard can be unavailable; the address bar still has the link */ }
}

const sources = computed(() => [...new Set(props.item?.changes.map(change => change.source) ?? [])]);

/** How an item's values reached the trace, as a phrase rather than a code. */
const sourcePhrases = { testside: "Recorded by the test", observed: "Observed in a response", applicationside: "Reported by the application" } as const;

/** Where an item came from - the operation that first recorded it - plays the part the path plays for a span. */
const origin = computed(() => props.item?.changes.find(change => change.span)?.span ?? null);

/** An item's lifetime, said once: a moment when it has none, a range with its length when it does. */
const lifetime = computed(() => {
  const item = props.item;
  if (!item) return "";
  const from = formatOffset(item.firstSeen - props.test.start);
  const length = item.lastSeen - item.firstSeen;
  return length < 0.5 ? `Recorded at ${from}` : `${from} to ${formatOffset(item.lastSeen - props.test.start)}, ${formatDuration(length)}`;
});

/** The id and scope, only where they add something: not the title again, not this test's own name. */
const itemOrigin = computed(() => {
  const item = props.item;
  if (!item) return "";
  const parts: string[] = [];
  const title = `${itemTitle(item)} ${item.name}`.toLocaleLowerCase();
  if (!title.includes(item.id.toLocaleLowerCase())) parts.push(item.id);
  if (item.scope && item.scope !== props.test.name) parts.push(`scope ${item.scope}`);
  return parts.join(", ");
});
</script>

<template>
  <aside class="inspector" aria-label="Details">
    <header class="head">
      <div class="top">
        <nav v-if="span && path.length" class="path" aria-label="Where this ran">
          <button v-for="ancestor in path" :key="ancestor.id" type="button" @click="emit('select', ancestor)">{{ ancestor.name }}</button>
        </nav>
        <span v-else-if="span" class="path-label">{{ span.phase.charAt(0).toUpperCase() + span.phase.slice(1) }} phase</span>
        <nav v-else-if="origin" class="path" aria-label="Where this came from">
          <button type="button" @click="origin && emit('select', origin)">Created by {{ origin.name }}</button>
        </nav>
        <span v-else class="path-label">State of {{ item?.test ? "this test" : "the run" }}</span>
        <span class="actions">
          <button type="button" class="link" :title="copied ? 'Link copied' : 'Copy a link to this'" @click="copyLink">{{ copied ? "Copied" : "Copy link" }}</button>
          <AppButton variant="icon" class="close" label="Close details" @click="emit('close')">×</AppButton>
        </span>
      </div>

      <template v-if="span">
        <div class="title">
          <KindChip :type="kindLabel(span.kind)" />
          <h2>{{ span.name }}</h2>
        </div>
        <p class="facts">
          <OutcomePill :outcome="span.status" />
          <span>{{ formatDuration(span.duration) }}</span>
          <span>{{ formatOffset(span.start - test.start) }} into the test</span>
          <span class="phase" :style="{ '--phase-color': `var(--phase-${span.phase})` }"><i />{{ span.phase }}</span>
          <span v-if="span.count > 1">ran {{ span.count }} times</span>
        </p>
        <p class="origin" :title="`${span.kind} from ${span.source}`"><code>{{ span.kind }}</code> from {{ span.source }}</p>
      </template>

      <template v-else-if="item">
        <div class="title">
          <KindChip :type="itemKindLabel(item)" />
          <h2>{{ itemTitle(item) }}</h2>
        </div>
        <p class="facts">
          <span>{{ lifetime }}</span>
          <span v-for="source in sources" :key="source" class="source" :class="source">{{ sourcePhrases[source] }}</span>
        </p>
        <p v-if="itemOrigin" class="origin" :title="itemOrigin">{{ itemOrigin }}</p>
      </template>
    </header>

    <div class="body">
      <SpanInspector v-if="span" :key="span.id" :span="span" :test="test"
                     @select="emit('select', $event)" @item="emit('item', $event)" @artifact="emit('artifact', $event)" />
      <ItemInspector v-else-if="item" :key="item.key" :item="item" :test="test" @select="emit('select', $event)" />
    </div>
  </aside>
</template>

<style scoped>
.inspector {
  min-width: 0;
  min-height: 0;
  display: grid;
  grid-template-rows: auto minmax(0, 1fr);
  border: 1px solid var(--border);
  border-radius: var(--radius-panel);
  background: var(--surface);
  overflow: hidden;
}
.head { padding: var(--space-2) var(--space-4) var(--space-4); display: grid; gap: var(--space-2); border-bottom: 1px solid var(--border); background: var(--surface); }
.top { min-height: 28px; display: flex; align-items: center; justify-content: space-between; gap: var(--space-3); }
.actions { flex: none; display: flex; align-items: center; gap: var(--space-1); }
.actions :deep(.close) { width: 28px; height: 28px; }
.link { height: 28px; padding: 0 var(--space-3); border: 1px solid transparent; border-radius: var(--radius-control); background: transparent; color: var(--muted); font-size: var(--text-micro); transition: color var(--motion-fast) var(--motion-ease), border-color var(--motion-fast) var(--motion-ease); }
.link:hover { border-color: var(--border); color: var(--text); }
.path { min-width: 0; display: flex; flex-wrap: wrap; gap: var(--space-1); }
.path button { padding: 0; border: 0; background: transparent; color: var(--muted); font-size: var(--text-micro); text-align: left; transition: color var(--motion-fast) var(--motion-ease); }
.path button:not(:last-child)::after { content: "/"; margin-left: var(--space-1); color: var(--dim); }
.path button:hover { color: var(--text); }
.path-label { color: var(--muted); font-size: var(--text-micro); font-weight: var(--weight-semibold); }

.title { min-width: 0; display: flex; align-items: flex-start; gap: var(--space-2); }
.title :deep(.chip) { flex: none; margin-top: var(--space-1); }
h2 { min-width: 0; font-size: var(--text-heading); letter-spacing: var(--tracking-display); line-height: var(--leading-tight); overflow-wrap: anywhere; }
.facts { display: flex; flex-wrap: wrap; align-items: center; gap: var(--space-1) var(--space-4); color: var(--muted); font-size: var(--text-meta); }
.phase { display: inline-flex; align-items: center; gap: var(--space-1); text-transform: capitalize; }
.phase i { width: 7px; height: 7px; border-radius: var(--radius-hairline); background: var(--phase-color); }
.source { padding: 0 var(--space-2); border-radius: var(--radius-chip); background: var(--surface-2); font-size: var(--text-micro); font-weight: var(--weight-semibold); }
.source.applicationside { background: var(--blueprint-soft); color: var(--blueprint); }
/* One line: where it came from is context, not content; the whole of it is in the tooltip. */
.origin { overflow: hidden; color: var(--dim); font-size: var(--text-micro); text-overflow: ellipsis; white-space: nowrap; }
.origin code { font-family: var(--font-mono); }

/* Rows are max-content: in a body of definite height, auto rows would squeeze a section down to the room left
   and cut it off, instead of letting the body scroll. */
.body {
  min-height: 0;
  padding: var(--space-4);
  padding-bottom: max(var(--space-5), env(safe-area-inset-bottom));
  display: grid;
  grid-auto-rows: max-content;
  align-content: start;
  overflow: auto;
  overscroll-behavior: contain;
  overflow-wrap: anywhere;
}
</style>
