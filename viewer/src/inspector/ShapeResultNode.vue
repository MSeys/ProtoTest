<script setup lang="ts">
import { computed, ref } from "vue";
import type { ShapeCheckNode } from "../model/trace-schema";

defineOptions({ name: "ShapeResultNode" });
const props = defineProps<{ node: ShapeCheckNode }>();
const collapsed = ref(false);
const property = computed(() => props.node.key.startsWith("[") ? props.node.key : `"${props.node.key}"`);
const diagnostic = computed(() => {
  const reason = props.node.mismatch?.reason?.trim() ?? "";
  return /^values? did not match\.?$/i.test(reason) ? "" : reason;
});
function literal(value: unknown): string {
  if (value === undefined) return "undefined";
  if (value !== null && typeof value === "object") return Array.isArray(value) ? "[…]" : "{…}";
  return JSON.stringify(value) ?? String(value);
}
</script>

<template>
  <div class="node" :class="node.status">
    <div class="row">
      <button v-if="node.children.length" type="button" class="toggle"
              :aria-label="collapsed ? 'Expand property' : 'Collapse property'" @click="collapsed = !collapsed">
        <svg viewBox="0 0 10 10" width="9" height="9" aria-hidden="true">
          <path d="M1.6 5H8.4" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" />
          <path v-if="collapsed" d="M5 1.6V8.4" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" />
        </svg>
      </button>
      <span v-else class="spacer" />
      <span class="icon" :aria-label="node.status">{{ node.status === "failed" ? "×" : node.status === "matched" ? "✓" : "·" }}</span>
      <span class="property">
        <strong>{{ property }}</strong><i>:</i>
        <template v-if="node.children.length"><code>{{ Array.isArray(node.actual ?? node.expected) ? "[" : "{" }}</code></template>
        <template v-else-if="node.status === 'failed'">
          <code class="expected" :title="`Expected ${literal(node.expected)}`">{{ literal(node.expected) }}</code>
          <b class="arrow">→</b>
          <code class="actual" :title="`Actual ${literal(node.actual)}`">{{ literal(node.actual) }}</code>
          <small v-if="diagnostic" :title="diagnostic">{{ diagnostic }}</small>
        </template>
        <code v-else class="matched" :title="literal(node.actual ?? node.expected)">{{ literal(node.actual ?? node.expected) }}</code>
      </span>
    </div>
    <div v-if="node.children.length && !collapsed" class="children">
      <ShapeResultNode v-for="child in node.children" :key="child.path" :node="child" />
    </div>
    <div v-if="node.children.length && !collapsed" class="close">{{ Array.isArray(node.actual ?? node.expected) ? "]" : "}" }}</div>
  </div>
</template>

<style scoped>
.node { position: relative; }
.row {
  min-height: 24px;
  display: grid;
  grid-template-columns: 16px 14px minmax(0, 1fr);
  align-items: center;
  gap: var(--space-1);
  border-radius: var(--radius-chip);
}
.row:hover { background: var(--hover); }
.toggle {
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
.toggle:hover { border-color: var(--blueprint); color: var(--text); }
.spacer { width: 14px; }
.icon { display: grid; place-items: center; color: var(--muted); font: var(--weight-bold) var(--text-meta)/1 var(--font-mono); }
.node.matched > .row > .icon { color: var(--success); }
.node.failed > .row > .icon { color: var(--danger); }

.property { min-width: 0; display: flex; align-items: baseline; gap: var(--space-1); white-space: nowrap; }
.property strong { color: var(--blueprint); font: var(--weight-semibold) var(--text-micro) var(--font-mono); }
.property > i { color: var(--muted); font-style: normal; }
.property code { min-width: 0; overflow: hidden; color: var(--text); font: var(--text-micro) var(--font-mono); text-overflow: ellipsis; }
.property small { min-width: 0; margin-left: var(--space-1); overflow: hidden; color: var(--dim); font: var(--text-micro) var(--font-mono); text-overflow: ellipsis; }
.expected { color: var(--muted); text-decoration: line-through; text-decoration-color: var(--danger-line); }
.actual { color: var(--danger); font-weight: var(--weight-bold); }
.arrow { color: var(--danger); font-size: var(--text-micro); }

/* The nesting is drawn with the same guide the execution tree uses. */
.children { margin-left: var(--space-3); padding-left: var(--space-3); border-left: 1px solid var(--border); }
.close { height: 18px; margin-left: var(--space-6); color: var(--muted); font: var(--text-micro)/18px var(--font-mono); }
</style>
