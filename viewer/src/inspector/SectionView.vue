<script setup lang="ts">
import { computed } from "vue";
import type { Section } from "../trace/model";
import JsonView from "./JsonView.vue";

/*
 * A section by its kind: fields as a definition list, code as a readable block, checks with their verdict,
 * a diff as expected against actual. Any other kind - an integration's own - still renders: its items as
 * fields and its content as a block. A section is never blank.
 */
// `brief` leaves out a failed check's full message, for when a richer view of the same failure sits beside it.
const props = defineProps<{ section: Section; brief?: boolean }>();

const known = ["fields", "code", "checks", "diff"];
const kind = computed(() => known.includes(props.section.kind) ? props.section.kind : "generic");
// JSON content opens as a tree; other code is shown as written.
const isJson = computed(() => props.section.language === "json" && Boolean(props.section.content));
</script>

<template>
  <JsonView v-if="kind === 'code' && isJson" :value="section.content" :label="section.label" :open-depth="1" />
  <section v-else class="section" :class="`is-${kind}`">
    <h3>{{ section.label }}<small v-if="kind === 'generic'">{{ section.kind }}</small></h3>

    <dl v-if="(kind === 'fields' || kind === 'generic') && section.items.length" class="fields">
      <template v-for="item in section.items" :key="item.label">
        <dt>{{ item.label }}</dt>
        <dd :class="item.tone">{{ item.value ?? "" }}<small v-if="item.detail">{{ item.detail }}</small></dd>
      </template>
    </dl>

    <ul v-else-if="kind === 'checks'" class="checks">
      <li v-for="item in section.items" :key="item.label" :class="item.tone">
        <span class="verdict">{{ item.tone === "error" ? "Failed" : item.tone === "success" ? "Passed" : item.tone === "warning" ? "Warning" : "Checked" }}</span>
        <strong>{{ item.label }}</strong>
        <span v-if="item.value" class="value">{{ item.value }}</span>
        <pre v-if="item.detail && item.tone === 'error' && !brief" class="detail">{{ item.detail }}</pre>
      </li>
    </ul>

    <table v-else-if="kind === 'diff'" class="diff">
      <thead><tr><th>Property</th><th>Expected</th><th>Actual</th></tr></thead>
      <tbody>
        <tr v-for="item in section.items" :key="item.label"><td>{{ item.label }}</td><td>{{ item.value }}</td><td class="actual">{{ item.detail }}</td></tr>
      </tbody>
    </table>

    <pre v-if="(kind === 'code' || kind === 'generic') && section.content" class="code"><code>{{ section.content }}</code></pre>
    <p v-if="!section.items.length && !section.content" class="empty">Recorded without content.</p>
  </section>
</template>

<style scoped>
.section { display: grid; gap: var(--space-2); }
h3 { display: flex; align-items: baseline; gap: var(--space-2); font-family: var(--font-ui); font-size: var(--text-meta); font-weight: var(--weight-bold); }
h3 small { color: var(--dim); font-size: var(--text-micro); font-weight: var(--weight-regular); }

.fields { margin: 0; display: grid; grid-template-columns: minmax(72px, max-content) minmax(0, 1fr); gap: 0 var(--space-4); }
.fields dt, .fields dd { padding: var(--space-1) 0; border-top: 1px solid var(--border); }
.fields dt { color: var(--muted); font-size: var(--text-micro); }
.fields dd { margin: 0; overflow-wrap: anywhere; font: var(--text-meta) var(--font-mono); }
.fields dd small { display: block; color: var(--muted); font-family: var(--font-ui); }
.fields dd.success { color: var(--success); }
.fields dd.warning { color: var(--warning); }
.fields dd.error { color: var(--danger); }

.checks { margin: 0; padding: 0; display: grid; gap: var(--space-1); list-style: none; }
.checks li { padding: var(--space-2) var(--space-3); display: flex; flex-wrap: wrap; align-items: baseline; gap: var(--space-1) var(--space-3); border-left: 2px solid var(--border-strong); background: var(--surface-2); }
.checks li.success { border-left-color: var(--success); }
.checks li.error { border-left-color: var(--danger); background: var(--danger-soft); }
.checks li.warning { border-left-color: var(--warning); background: var(--warning-soft); }
.verdict { color: var(--muted); font-size: var(--text-micro); font-weight: var(--weight-semibold); }
.success .verdict { color: var(--success); }
.error .verdict { color: var(--danger); }
.checks strong { font-size: var(--text-meta); }
.value { color: var(--muted); font-size: var(--text-meta); }
.detail { flex-basis: 100%; margin: 0; white-space: pre-wrap; overflow-wrap: anywhere; font: var(--text-micro)/var(--leading) var(--font-mono); }

.diff { width: 100%; border-collapse: collapse; font-size: var(--text-meta); }
.diff th { padding-bottom: var(--space-1); color: var(--muted); font-size: var(--text-micro); text-align: left; }
.diff td { padding: var(--space-1) var(--space-3) var(--space-1) 0; border-top: 1px solid var(--border); overflow-wrap: anywhere; font-family: var(--font-mono); }
.diff .actual { color: var(--danger); }

.code {
  margin: 0;
  padding: var(--space-3);
  border-radius: var(--radius-chip);
  background: var(--surface-sunken);
  color: var(--text-on-sunken);
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  font: var(--text-micro)/var(--leading) var(--font-mono);
}
.empty { color: var(--dim); font-size: var(--text-meta); }
</style>
