<script setup lang="ts">
import { computed, ref } from "vue";
import type { Artifact } from "../trace/model";
import { formatBytes } from "../trace/format";
import TextInput from "./TextInput.vue";
import FilterChip from "./FilterChip.vue";
import EmptyState from "./EmptyState.vue";

/** One row: the file, who produced it, and the line under its name. */
export interface FileEntry {
  artifact: Artifact;
  /** Who produced it, when one list holds several producers; a single test needs no column. */
  owner?: string;
  detail: string | null;
}

const props = defineProps<{ entries: FileEntry[] }>();
const emit = defineEmits<{ open: [artifact: Artifact] }>();

const owned = computed(() => props.entries.some(entry => entry.owner));
const query = ref("");
const visible = computed(() => {
  const needle = query.value.trim().toLowerCase();
  if (!needle) return props.entries;
  return props.entries.filter(entry => [entry.artifact.name, entry.artifact.mediaType, entry.owner ?? "", entry.detail ?? ""]
    .some(field => field.toLowerCase().includes(needle)));
});
</script>

<template>
  <div class="file-list" :class="{ owned }">
    <TextInput v-if="entries.length > 8" v-model="query" type="search" placeholder="Find a file" label="Find a file" class="find" />
    <div v-if="visible.length" class="files">
      <button v-for="entry in visible" :key="entry.artifact.id" type="button" class="file-row"
              :disabled="Boolean(entry.artifact.error)" @click="emit('open', entry.artifact)">
        <b v-if="owned">{{ entry.owner }}</b>
        <span class="file-main">
          <strong>{{ entry.artifact.name }}</strong>
          <small v-if="entry.detail">{{ entry.detail }}</small>
        </span>
        <small class="file-type">{{ entry.artifact.mediaType }}</small>
        <small class="file-size">{{ entry.artifact.error ?? formatBytes(entry.artifact.sizeBytes) }}</small>
      </button>
    </div>
    <EmptyState v-else message="No file matches this search.">
      <FilterChip label="Show all files" @select="query = ''" />
    </EmptyState>
  </div>
</template>

<style scoped>
.file-list { display: grid; container-type: inline-size; }
.find { margin: var(--space-3) var(--space-4); }
.files { display: grid; }
.owned .file-row { grid-template-columns: 4ch minmax(0, 1fr) auto auto; }
.file-row {
  padding: var(--space-2) var(--space-4);
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto auto;
  align-items: center;
  gap: var(--space-3);
  border: 0;
  border-top: 1px solid var(--border);
  background: transparent;
  color: var(--text);
  text-align: left;
  cursor: pointer;
}
.file-row:first-child { border-top: 0; }
.file-row:hover:not(:disabled) { background: var(--hover); }
.file-row:disabled { cursor: default; }
.file-row > b { color: var(--dim); font: var(--text-micro) var(--font-mono); }
.file-main { min-width: 0; display: grid; }
.file-main strong { overflow: hidden; font-size: var(--text-meta); font-weight: var(--weight-semibold); text-overflow: ellipsis; white-space: nowrap; }
.file-main small { overflow: hidden; color: var(--muted); font-size: var(--text-micro); text-overflow: ellipsis; white-space: nowrap; }
.file-type { color: var(--dim); font: var(--text-micro) var(--font-mono); }
.file-size { color: var(--muted); font: var(--text-micro) var(--font-mono); white-space: nowrap; }

@container (max-width: 34rem) {
  .file-type { display: none; }
}
</style>
