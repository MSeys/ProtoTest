<script setup lang="ts">
import { computed, nextTick, ref, watch } from "vue";
import type { Span, TestTrace } from "../trace/model";
import { formatDuration, formatOffset, kindLabel, tone } from "../trace/format";
import KindChip from "../ui/KindChip.vue";
import TextInput from "../ui/TextInput.vue";
import EmptyState from "../ui/EmptyState.vue";
import Panel from "../ui/Panel.vue";

/*
 * Every span, as recorded, in one indented list: the complete fallback when the story's folding hides
 * something. Events on a span are listed under it. A search keeps a match's ancestors, so a hit is never
 * shown out of context.
 */
const props = defineProps<{ test: TestTrace; selected?: string }>();
const emit = defineEmits<{ select: [span: Span] }>();

const query = ref("");
const folded = ref(new Set<string>());
watch(() => props.test.id, () => { folded.value = new Set(); });

const matches = computed(() => {
  const text = query.value.trim().toLocaleLowerCase();
  if (!text) return null;
  const keep = new Set<string>();
  for (const span of props.test.spans) {
    const haystack = [span.name, span.kind, span.source, ...Object.values(span.attributes)].join(" ").toLocaleLowerCase();
    if (!haystack.includes(text)) continue;
    for (let current: Span | null = span; current; current = current.parent) keep.add(current.id);
  }
  return keep;
});

const rows = computed(() => {
  const result: Span[] = [];
  const walk = (spans: Span[]) => spans.forEach(span => {
    if (matches.value && !matches.value.has(span.id)) return;
    result.push(span);
    if (!folded.value.has(span.id) || matches.value) walk(span.children);
  });
  walk(props.test.roots);
  return result;
});

// A selection made elsewhere scrolls its row into view, so the tree never hides what the details show.
const list = ref<HTMLElement>();
watch(() => props.selected, () => void nextTick(() => list.value?.querySelector(".row.active")?.scrollIntoView({ block: "nearest" })), { immediate: true });

function toggle(id: string) {
  const next = new Set(folded.value);
  if (next.has(id)) next.delete(id); else next.add(id);
  folded.value = next;
}
</script>

<template>
  <Panel class="spans" title="Every span" :subtitle="`${test.spans.length} spans as recorded, in the order they started.`" pad="none">
    <template #actions>
      <TextInput v-model="query" type="search" placeholder="Find by name, kind or attribute" label="Find a span" class="find" />
    </template>
    <div v-if="rows.length" ref="list" class="list" role="tree">
      <div v-for="span in rows" :key="span.id" class="row" :class="[tone(span.status), { active: span.id === selected }]"
           role="treeitem" :aria-level="span.depth + 1" :aria-expanded="span.children.length ? !folded.has(span.id) : undefined"
           :style="{ '--depth': span.depth }">
        <button v-if="span.children.length && !matches" type="button" class="fold" :aria-label="folded.has(span.id) ? 'Unfold' : 'Fold'"
                @click="toggle(span.id)">{{ folded.has(span.id) ? "+" : "−" }}</button>
        <span v-else class="fold" />
        <button type="button" class="pick" :data-span="span.id" @click="emit('select', span)">
          <KindChip :type="kindLabel(span.kind)" />
          <span class="name">{{ span.name }}</span>
          <span class="kind">{{ span.kind }}</span>
          <span v-if="span.moments.length + span.evidence.length" class="events">{{ span.moments.length + span.evidence.length }} {{ span.moments.length + span.evidence.length === 1 ? "event" : "events" }}</span>
        </button>
        <span class="offset">{{ formatOffset(span.start - test.start) }}</span>
        <span class="duration">{{ formatDuration(span.duration) }}</span>
        <i class="status" :class="tone(span.status)" />
      </div>
    </div>
    <EmptyState v-else message="No span matches this search." />
  </Panel>
</template>

<style scoped>
.spans { container-type: inline-size; }
.find { width: clamp(160px, 40%, 300px); }
.list { padding: var(--space-2); }
.row {
  min-height: var(--row-height);
  padding-left: calc(var(--depth) * var(--space-5));
  transition: background var(--motion-fast) var(--motion-ease);
  display: grid;
  grid-template-columns: 18px minmax(0, 1fr) 64px 56px 7px;
  align-items: center;
  gap: var(--space-2);
  border-radius: var(--radius-chip);
}
.row:hover { background: var(--hover); }
.row.active { background: var(--blueprint-soft); box-shadow: inset 2px 0 0 var(--blueprint); }
.row.danger { background: var(--danger-soft); }
.fold { width: 16px; height: 16px; padding: 0; display: grid; place-items: center; border: 0; border-radius: var(--radius-hairline); background: transparent; color: var(--muted); font-size: var(--text-meta); }
button.fold:hover { background: var(--surface-2); color: var(--text); }
.pick { min-width: 0; padding: 0; display: flex; align-items: center; gap: var(--space-2); border: 0; background: transparent; text-align: left; }
.name { flex: 0 1 auto; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: var(--text-meta); }
.kind { flex: 1 1 0; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--dim); font: var(--text-micro) var(--font-mono); }
.events { flex: none; color: var(--muted); font-size: var(--text-micro); }
.offset, .duration { color: var(--muted); font-size: var(--text-micro); text-align: right; font-variant-numeric: tabular-nums; }

@container (max-width: 560px) {
  .row { grid-template-columns: 18px minmax(0, 1fr) 52px 7px; padding-left: calc(var(--depth) * 10px); }
  .offset, .kind { display: none; }
}
</style>
