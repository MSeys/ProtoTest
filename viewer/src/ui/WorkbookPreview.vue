<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { columnName, readWorkbook } from "../artifacts/workbook";
import type { WorkbookPreview } from "../artifacts/workbook";
import EmptyState from "./EmptyState.vue";

const props = defineProps<{ blob: Blob }>();
const workbook = ref<WorkbookPreview>();
const selected = ref(0);
const loading = ref(false);
const error = ref("");
let load = 0;

const sheet = computed(() => workbook.value?.sheets[selected.value]);

watch(() => props.blob, async blob => {
  const current = ++load;
  loading.value = true;
  error.value = "";
  workbook.value = undefined;
  selected.value = 0;
  try {
    const result = await readWorkbook(blob);
    if (current === load) workbook.value = result;
  } catch (reason) {
    if (current === load) error.value = reason instanceof Error ? reason.message : "The workbook could not be previewed.";
  } finally {
    if (current === load) loading.value = false;
  }
}, { immediate: true });
</script>

<template>
  <EmptyState v-if="loading" message="Reading workbook…" />
  <div v-else-if="error" class="failure"><strong>Workbook preview unavailable</strong><p>{{ error }}</p></div>
  <div v-else-if="workbook && sheet" class="workbook">
    <div class="workbook-bar">
      <div class="tabs" role="tablist" aria-label="Workbook sheets">
        <button v-for="(candidate, index) in workbook.sheets" :key="candidate.name" type="button" role="tab"
                :aria-selected="selected === index" :class="{ active: selected === index }" @click="selected = index">
          {{ candidate.name }}<span v-if="candidate.hidden">hidden</span>
        </button>
      </div>
      <small>{{ sheet.rowCount }} rows × {{ sheet.columnCount }} columns</small>
    </div>
    <div class="grid" role="region" :aria-label="`${sheet.name} worksheet preview`" tabindex="0">
      <table>
        <thead>
          <tr>
            <th class="corner" aria-hidden="true"></th>
            <th v-for="column in sheet.rows[0]?.length ?? 0" :key="column" scope="col">{{ columnName(column - 1) }}</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(row, rowIndex) in sheet.rows" :key="rowIndex">
            <th scope="row">{{ rowIndex + 1 }}</th>
            <td v-for="(cell, columnIndex) in row" :key="columnIndex" :title="cell">{{ cell }}</td>
          </tr>
        </tbody>
      </table>
      <div v-if="!sheet.rows.length" class="empty-sheet">This sheet is empty.</div>
    </div>
    <p v-if="sheet.truncated" class="notice">Preview limited to the first 500 rows, 100 columns and 50,000 cells. Download the workbook for the complete file.</p>
  </div>
</template>

<style scoped>
.workbook { min-width: 0; min-height: 0; height: 100%; display: grid; grid-template-rows: auto minmax(0, 1fr) auto; border: 1px solid var(--border); border-radius: var(--radius-control); background: var(--surface); overflow: hidden; }
.workbook-bar { min-width: 0; display: flex; align-items: center; justify-content: space-between; gap: var(--space-4); padding: var(--space-2) var(--space-3); border-bottom: 1px solid var(--border); background: var(--surface-2); }
.tabs { min-width: 0; display: flex; gap: var(--space-1); overflow-x: auto; }
.tabs button { flex: none; min-height: var(--control-height); padding: 0 var(--space-3); border: 1px solid transparent; border-radius: var(--radius-chip); background: transparent; color: var(--muted); font: var(--weight-medium) var(--text-meta)/1 var(--font-ui); cursor: pointer; }
.tabs button:hover { background: var(--hover); color: var(--text); }
.tabs button.active { border-color: var(--blueprint-line); background: var(--blueprint-soft); color: var(--blueprint); }
.tabs button span { margin-left: var(--space-2); color: var(--dim); font: var(--text-micro)/1 var(--font-mono); text-transform: uppercase; }
.workbook-bar > small { flex: none; color: var(--dim); font: var(--text-micro)/1 var(--font-mono); }
.grid { min-width: 0; min-height: 0; overflow: auto; background: var(--surface); }
table { min-width: 100%; border-collapse: separate; border-spacing: 0; table-layout: auto; font-size: var(--text-meta); }
th, td { min-width: 92px; max-width: 360px; height: 30px; padding: var(--space-1) var(--space-3); border-right: 1px solid var(--border); border-bottom: 1px solid var(--border); overflow: hidden; color: var(--text); text-align: left; text-overflow: ellipsis; white-space: nowrap; }
thead th { position: sticky; top: 0; z-index: 2; min-width: 48px; background: var(--surface-2); color: var(--dim); font: var(--text-micro)/1 var(--font-mono); text-align: center; }
tbody th { position: sticky; left: 0; z-index: 1; min-width: 48px; width: 48px; background: var(--surface-2); color: var(--dim); font: var(--text-micro)/1 var(--font-mono); text-align: right; }
.corner { left: 0; z-index: 3; min-width: 48px; width: 48px; }
tbody tr:first-child td { background: var(--blueprint-soft); color: var(--text); font-weight: var(--weight-semibold); }
tbody tr:hover td { background: var(--hover); }
.empty-sheet { display: grid; min-height: 180px; place-items: center; color: var(--muted); font-size: var(--text-meta); }
.notice { margin: 0; padding: var(--space-2) var(--space-3); border-top: 1px solid var(--warning-line); background: var(--warning-soft); color: var(--warning); font-size: var(--text-micro); }
.failure { padding: var(--space-4); border: 1px solid var(--danger); border-radius: var(--radius-control); background: var(--danger-soft); font-size: var(--text-meta); }
.failure strong { display: block; color: var(--danger); }
.failure p { margin-top: var(--space-1); color: var(--muted); }
@media (max-width: 720px) {
  .workbook-bar { align-items: flex-start; flex-direction: column; gap: var(--space-2); }
}
</style>
