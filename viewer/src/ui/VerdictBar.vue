<script setup lang="ts">
import { computed } from "vue";
import type { Span, TestTrace } from "../trace/model";
import { diagnosisRule, diagnosisRuleLabels, testFindings } from "../trace/analysis";
import { formatOffset, jsonLiteral, outcomeLabel, tone } from "../trace/format";

/*
 * Why a test did not pass, in one block and in the words `prototest summary` uses: the rule, the check or
 * operation that decided it, the call it judged, where it happened, and the first thing that differed.
 * The full comparison lives in the details, which open on the failing check where they dock.
 */
const props = defineProps<{ test: TestTrace }>();
const emit = defineEmits<{ select: [span: Span] }>();

const failure = computed(() => props.test.failure);
const finding = computed(() => testFindings(props.test)[0] ?? null);
const rule = computed(() => diagnosisRule(props.test));
const label = computed(() => rule.value ? diagnosisRuleLabels[rule.value] : outcomeLabel(props.test.outcome));

const where = computed(() => {
  const span = failure.value?.span;
  if (span) return `${span.phase.charAt(0).toUpperCase()}${span.phase.slice(1)}, ${formatOffset(span.start - props.test.start)} into the test`;
  if (finding.value) return `Recorded ${formatOffset(finding.value.at - props.test.start)} into the test`;
  return "";
});

/** The first difference, or the first line of the error, or what the finding is about. */
const detail = computed(() => {
  const current = failure.value;
  if (current) {
    const mismatch = current.mismatches[0];
    if (mismatch) {
      const more = current.mismatches.length - 1;
      return { path: mismatch.path, expected: literal(mismatch.expected), actual: literal(mismatch.actual), more };
    }
    return current.span.error?.message.split(/\r?\n/)[0] ?? current.check?.detail ?? current.check?.value ?? "";
  }
  if (finding.value) return [finding.value.status, finding.value.category, ...finding.value.tags].filter(Boolean).join(", ");
  return props.test.outcome === "partial" ? "The test finished with a partial result and recorded nothing to blame." : "No failing operation was recorded.";
});

function literal(value: unknown): string {
  return value === undefined ? "missing" : jsonLiteral(value);
}
</script>

<template>
  <section class="verdict" :class="tone(test.outcome)" aria-label="Why this test did not pass">
    <span class="rule">{{ label }}</span>
    <button v-if="failure" type="button" class="what" @click="emit('select', failure.span)">{{ failure.span.name }}</button>
    <strong v-else class="what">{{ finding?.message ?? outcomeLabel(test.outcome) }}</strong>
    <button v-if="failure?.call && failure.call !== failure.span" type="button" class="call" @click="emit('select', failure.call)">
      on {{ failure.call.name }}
    </button>
    <button v-else-if="finding?.span" type="button" class="call" @click="finding.span && emit('select', finding.span)">
      on {{ finding.span.name }}
    </button>
    <span v-if="where" class="where">{{ where }}</span>
    <p v-if="typeof detail === 'string' ? detail : true" class="detail">
      <template v-if="typeof detail === 'string'">{{ detail }}</template>
      <template v-else>{{ detail.path }}: expected {{ detail.expected }}, got <b>{{ detail.actual }}</b><template v-if="detail.more > 0">, and {{ detail.more }} more</template></template>
    </p>
  </section>
</template>

<style scoped>
.verdict {
  padding: var(--space-3) var(--space-4);
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--space-2) var(--space-3);
  border: 1px solid var(--border);
  border-left: 3px solid var(--dim);
  border-radius: var(--radius-control);
  background: var(--surface);
}
.verdict.danger { border-left-color: var(--danger); background: var(--danger-soft); }
.verdict.warning { border-left-color: var(--warning); background: var(--warning-soft); }
.rule { padding: 0 var(--space-2); border: 1px solid currentColor; border-radius: var(--radius-chip); color: var(--muted); font-size: var(--text-meta); font-weight: var(--weight-bold); line-height: 1.7; white-space: nowrap; }
.danger .rule { color: var(--danger); }
.warning .rule { color: var(--warning); }
button { padding: 0; border: 0; background: transparent; text-align: left; }
.what { min-width: 0; overflow-wrap: anywhere; font-size: var(--text-title); font-weight: var(--weight-bold); }
button.what:hover, .call:hover { text-decoration: underline; }
.call { color: var(--muted); font-size: var(--text-body); }
.where { margin-left: auto; color: var(--muted); font-size: var(--text-meta); white-space: nowrap; }
.detail { flex: 1 1 100%; min-width: 0; overflow-wrap: anywhere; font: var(--text-body)/var(--leading) var(--font-mono); }
.danger .detail b { color: var(--danger); }
.warning .detail b { color: var(--warning); }
</style>
