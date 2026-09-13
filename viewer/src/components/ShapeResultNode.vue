<script setup lang="ts">
import { computed, ref } from "vue";
import type { ShapeCheckNode } from "../trace-schema";

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
  <div class="shape-tree-node" :class="node.status">
    <div class="shape-tree-row">
      <button v-if="node.children.length" type="button" class="shape-tree-toggle"
              :aria-label="collapsed ? 'Expand property' : 'Collapse property'" @click="collapsed = !collapsed">
        {{ collapsed ? '+' : '−' }}
      </button>
      <span v-else class="shape-tree-spacer" />
      <span class="shape-result-icon" :aria-label="node.status">{{ node.status === 'failed' ? '×' : node.status === 'matched' ? '✓' : '·' }}</span>
      <span class="shape-property">
        <strong>{{ property }}</strong><i>:</i>
        <template v-if="node.children.length"><code>{{ Array.isArray(node.actual ?? node.expected) ? '[' : '{' }}</code></template>
        <template v-else-if="node.status === 'failed'">
          <code class="shape-expected" :title="`Expected ${literal(node.expected)}`">{{ literal(node.expected) }}</code>
          <b class="shape-value-arrow">→</b>
          <code class="shape-actual" :title="`Actual ${literal(node.actual)}`">{{ literal(node.actual) }}</code>
          <small v-if="diagnostic" :title="diagnostic">{{ diagnostic }}</small>
        </template>
        <code v-else class="shape-matched-value" :title="literal(node.actual ?? node.expected)">{{ literal(node.actual ?? node.expected) }}</code>
      </span>
    </div>
    <div v-if="node.children.length && !collapsed" class="shape-tree-children">
      <ShapeResultNode v-for="child in node.children" :key="child.path" :node="child" />
    </div>
    <div v-if="node.children.length && !collapsed" class="shape-tree-close">{{ Array.isArray(node.actual ?? node.expected) ? ']' : '}' }}</div>
  </div>
</template>
