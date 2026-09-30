<script setup lang="ts">
/*
 * Keys and their values, as rows: one line under each row across both columns, the keys aligned down the list,
 * values as text. Every key-value list in the details uses it - an item's state, request and response fields,
 * attributes, metadata - so they all read alike. A value slot lets a caller render a document instead of text.
 */
export interface Property {
  key: string;
  value: string | null;
  /** What the row shows for the key, when shorter than the key itself. */
  label?: string;
  tone?: "neutral" | "success" | "warning" | "error" | null;
  detail?: string | null;
}

withDefaults(defineProps<{
  entries: Property[];
  /** Keys and values in the code face, for raw attributes and metadata. */
  mono?: boolean;
  /** Rows without side padding, for a list that sits in running text rather than in a card. */
  inline?: boolean;
}>(), { mono: false, inline: false });
</script>

<template>
  <dl class="properties" :class="{ mono, inline }">
    <div v-for="entry in entries" :key="entry.key" class="row">
      <dt :title="entry.key">{{ entry.label ?? entry.key }}</dt>
      <dd :class="[entry.tone, { null: entry.value === null }]">
        <slot name="value" :entry="entry">{{ entry.value ?? "null" }}</slot>
        <small v-if="entry.detail">{{ entry.detail }}</small>
      </dd>
    </div>
  </dl>
</template>

<style scoped>
/* Rows share one set of columns through subgrid, so each row can draw a single line under both cells. */
.properties { min-width: 0; margin: 0; display: grid; grid-template-columns: fit-content(40%) minmax(0, 1fr); }
.row { grid-column: 1 / -1; display: grid; grid-template-columns: subgrid; column-gap: var(--space-4); padding: var(--space-2) var(--space-3); }
.row + .row { border-top: 1px solid var(--border); }
.inline .row { padding-inline: 0; }
dt, dd { min-width: 0; font-size: var(--text-meta); line-height: var(--leading); overflow-wrap: anywhere; }
dt { color: var(--muted); }
dd { margin: 0; color: var(--text); }
dd.null { color: var(--dim); }
dd.success { color: var(--success); }
dd.warning { color: var(--warning); }
dd.error { color: var(--danger); }
dd small { display: block; color: var(--muted); font-family: var(--font-ui); font-size: var(--text-micro); }
.mono dt, .mono dd { font-family: var(--font-mono); font-size: var(--text-micro); }
</style>
