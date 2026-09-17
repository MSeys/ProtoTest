<script setup lang="ts">
import type { TraceEntry, TraceTreeItem } from "../model/trace-schema";
import { entryTitle, formatDuration, milliseconds, tone } from "../model/trace-format";
import { nodeType } from "../model/trace-levels";
import KindChip from "./KindChip.vue";

const props = withDefaults(defineProps<{
  items: TraceTreeItem[];
  depth: number;
  collapsed: Set<string>;
  selected?: TraceEntry;
  /** Ancestor ids of the selected entry: their guides light up so the path to a selection stays drawn. */
  path?: Set<string>;
  /** One entry per ancestor level: its id, and whether its branch continues below this row. */
  guides?: { id: string; continues: boolean }[];
  /** The span every row's share bar is measured against, in milliseconds. */
  span?: number;
}>(), { span: 0 });
const emit = defineEmits<{ select: [entry: TraceEntry]; toggle: [id: string] }>();

function share(entry: TraceEntry): number {
  if (!props.span) return 0;
  return Math.min(100, (milliseconds(entry.duration) / props.span) * 100);
}

/** One handler on the root instance drives keyboard navigation for the whole tree. */
function onKeydown(event: KeyboardEvent) {
  if (props.depth !== 0) return;
  const rows = [...(event.currentTarget as HTMLElement).querySelectorAll<HTMLElement>("[data-row]")];
  const current = rows.indexOf(document.activeElement as HTMLElement);
  if (event.key === "ArrowDown" || event.key === "ArrowUp") {
    event.preventDefault();
    rows[current + (event.key === "ArrowDown" ? 1 : -1)]?.focus();
    return;
  }
  const id = (document.activeElement as HTMLElement | null)?.dataset.id;
  if (!id) return;
  if (event.key === "ArrowRight" && props.collapsed.has(id)) { event.preventDefault(); emit("toggle", id); }
  if (event.key === "ArrowLeft" && !props.collapsed.has(id)) { event.preventDefault(); emit("toggle", id); }
}
</script>

<template>
  <div class="tree" @keydown="onKeydown">
    <template v-for="(item, index) in items" :key="item.entry.id">
      <div class="row" :class="[tone(item.entry.outcome), { active: item.entry.id === selected?.id }]">
        <!-- The trace: a drawn line from the operation that carried this one, not an indent you infer. -->
        <span class="trace" aria-hidden="true">
          <i v-for="guide in (guides ?? [])" :key="guide.id"
             class="guide" :class="{ continues: guide.continues, lit: path?.has(guide.id) }" />
          <i v-if="depth" class="elbow" :class="{ last: index === items.length - 1 }" />
        </span>

        <button v-if="item.children.length" type="button" class="expand"
                :aria-expanded="!collapsed.has(item.entry.id)"
                :aria-label="collapsed.has(item.entry.id) ? 'Expand' : 'Collapse'"
                @click="emit('toggle', item.entry.id)">
          <svg viewBox="0 0 10 10" width="10" height="10" aria-hidden="true">
            <path d="M1.6 5H8.4" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" />
            <path class="stem" d="M5 1.6V8.4" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" />
          </svg>
        </button>
        <span v-else class="node" :class="{ event: item.entry.entryKind === 'Event' }"
              :style="{ '--node-color': `var(--type-${nodeType(item.entry).id}, var(--type-custom))` }" aria-hidden="true" />

        <button type="button" class="pick" :data-row="true" :data-id="item.entry.id"
                :title="item.entry.name" @click="emit('select', item.entry)">
          <span class="name">{{ entryTitle(item.entry) }}</span>
          <KindChip :type="nodeType(item.entry)" />
          <span class="share" :title="`${share(item.entry).toFixed(1)}% of the test`">
            <i :style="{ width: `${share(item.entry) > 0 ? Math.max(share(item.entry), 3) : 0}%` }" />
          </span>
          <span class="time">{{ item.entry.duration ? formatDuration(milliseconds(item.entry.duration)) : "" }}</span>
          <i class="status" :class="tone(item.entry.outcome)" />
        </button>
      </div>

      <TraceTree v-if="item.children.length && !collapsed.has(item.entry.id)"
                 :items="item.children" :depth="depth + 1" :collapsed="collapsed" :selected="selected" :path="path"
                 :span="span"
                 :guides="[...(guides ?? []), { id: item.entry.id, continues: index !== items.length - 1 }]"
                 @select="emit('select', $event)" @toggle="emit('toggle', $event)" />
    </template>
  </div>
</template>

<style scoped>
/* The tree is drawn as an execution trace: guides are the branch a row hangs from, the node is what kind of
   thing it is, and the share column is how much of the test it took. */
.row {
  position: relative;
  height: var(--row-height);
  display: grid;
  grid-template-columns: auto 16px minmax(0, 1fr);
  align-items: center;
  gap: var(--space-1);
  border-radius: var(--radius-chip);
}
.row:hover { background: var(--hover); }
.row.active { background: var(--blueprint-soft); box-shadow: inset 2px 0 0 var(--blueprint); }
.row.danger .name { color: var(--danger); }

.trace { align-self: stretch; display: flex; }
.guide, .elbow { position: relative; width: 15px; flex: none; }
.guide.continues::before,
.elbow::before {
  content: "";
  position: absolute;
  left: 7px;
  top: 0;
  bottom: 0;
  border-left: 1px solid var(--border);
}
.guide.continues.lit::before { border-color: var(--blueprint-line); }
/* The last child stops its vertical run at the elbow instead of running past it. */
.elbow.last::before { bottom: 50%; }
.elbow::after {
  content: "";
  position: absolute;
  left: 7px;
  top: 50%;
  width: 8px;
  border-top: 1px solid var(--border);
}

.expand {
  width: 14px;
  height: 14px;
  display: grid;
  place-items: center;
  padding: 0;
  border: 1px solid var(--border-strong);
  border-radius: var(--radius-hairline);
  background: var(--surface);
  color: var(--muted);
}
.expand:hover { border-color: var(--blueprint); color: var(--text); }
.expand[aria-expanded="true"] .stem { opacity: 0; }
.expand .stem { transition: opacity var(--motion-fast) var(--motion-ease); }
/* A leaf is a checkpoint on the trace: a diamond, hollow when the entry is an instant event. */
.node {
  width: 7px;
  height: 7px;
  margin: 0 auto;
  border: 1px solid var(--node-color, var(--dim));
  background: var(--node-color, var(--dim));
  transform: rotate(45deg);
}
.node.event { background: var(--surface); }

.pick {
  position: relative;
  min-width: 0;
  height: 100%;
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto 44px 54px 7px;
  align-items: center;
  gap: var(--space-3);
  padding: 0 var(--space-2);
  border: 0;
  background: transparent;
  text-align: left;
}
.name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: var(--text-body); }
.time { text-align: right; color: var(--muted); font: var(--text-micro) var(--font-mono); }
/* How much of the test this operation took, as a column so the values compare down the view. */
.share { height: 4px; border-radius: var(--radius-hairline); background: var(--surface-2); overflow: hidden; }
.share i { display: block; height: 100%; border-radius: inherit; background: color-mix(in srgb, var(--blueprint) 65%, transparent); }
.row.danger .share i { background: var(--danger); }
.row.warning .share i { background: var(--warning); }

@container (max-width: 620px) {
  .pick { grid-template-columns: minmax(0, 1fr) 54px 7px; }
  .pick > span:nth-child(2), .share { display: none; }
}
</style>
