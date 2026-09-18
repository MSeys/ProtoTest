<script setup lang="ts">
import { computed } from "vue";
import type { Failure, Outcome, Span } from "../trace/model";
import { kindLabel, tone } from "../trace/format";
import KindChip from "./KindChip.vue";
import ShapeResultTree from "../inspector/ShapeResultTree.vue";
import { shapeTreeOf } from "../trace/shapes";

/*
 * The answer a reader opened a failing test for, before anything else: which check failed, the values it
 * compared, and the call that produced them. Everything here is one click from the operation it names.
 */
const props = defineProps<{ failure: Failure; outcome: Outcome }>();
const emit = defineEmits<{ select: [span: Span] }>();

// A shape check shows the document it validated, the same tree the inspector uses.
const shapeTree = computed(() => shapeTreeOf(props.failure.span));

const message = computed(() => {
  if (props.failure.mismatches.length) return "";
  return props.failure.span.error?.message ?? props.failure.check?.detail ?? props.failure.check?.value ?? "";
});

function value(input: unknown): string {
  if (input === undefined) return "missing";
  return typeof input === "string" ? JSON.stringify(input) : JSON.stringify(input) ?? String(input);
}
</script>

<template>
  <section class="failure" :class="tone(outcome)" aria-label="Why this test did not pass">
    <header>
      <i class="status" :class="tone(outcome)" />
      <KindChip :type="kindLabel(failure.span.kind)" />
      <button type="button" class="what" @click="emit('select', failure.span)">{{ failure.span.name }}</button>
      <span v-if="failure.check?.value" class="verdict">{{ failure.check.value }}</span>
      <button v-if="failure.call && failure.call !== failure.span" type="button" class="call" @click="emit('select', failure.call)">
        on {{ failure.call.name }}
      </button>
    </header>

    <ShapeResultTree v-if="shapeTree" :nodes="shapeTree" />
    <table v-else-if="failure.mismatches.length" class="mismatches">
      <thead><tr><th>Property</th><th>Expected</th><th>Actual</th></tr></thead>
      <tbody>
        <tr v-for="mismatch in failure.mismatches" :key="mismatch.path" :title="mismatch.reason">
          <td>{{ mismatch.path }}</td>
          <td class="expected">{{ value(mismatch.expected) }}</td>
          <td class="actual">{{ value(mismatch.actual) }}</td>
        </tr>
      </tbody>
    </table>
    <pre v-else-if="message" class="message">{{ message }}</pre>
  </section>
</template>

<style scoped>
.failure {
  padding: var(--space-3) var(--space-4);
  display: grid;
  gap: var(--space-3);
  border: 1px solid var(--border);
  border-left: 3px solid var(--dim);
  border-radius: var(--radius-control);
  background: var(--surface);
}
.failure.danger { border-left-color: var(--danger); background: var(--danger-soft); }
.failure.warning { border-left-color: var(--warning); background: var(--warning-soft); }
header { min-width: 0; display: flex; flex-wrap: wrap; align-items: center; gap: var(--space-2) var(--space-3); }
header button { padding: 0; border: 0; background: transparent; text-align: left; }
.what { font-size: var(--text-strong); font-weight: var(--weight-bold); }
.what:hover, .call:hover { text-decoration: underline; }
.verdict { color: var(--danger); font-size: var(--text-meta); font-weight: var(--weight-semibold); }
.warning .verdict { color: var(--warning); }
.call { color: var(--muted); font-size: var(--text-meta); }

.mismatches { width: 100%; border-collapse: collapse; font-size: var(--text-meta); }
.mismatches th { padding: 0 var(--space-3) var(--space-1) 0; color: var(--muted); font-size: var(--text-micro); font-weight: var(--weight-semibold); text-align: left; }
.mismatches td { padding: var(--space-1) var(--space-3) var(--space-1) 0; border-top: 1px solid var(--border); overflow-wrap: anywhere; font-family: var(--font-mono); vertical-align: top; }
.mismatches .expected { color: var(--muted); }
.mismatches .actual { color: var(--danger); font-weight: var(--weight-bold); }
.message { margin: 0; white-space: pre-wrap; overflow-wrap: anywhere; color: var(--text); font: var(--text-meta)/var(--leading) var(--font-mono); }
</style>
