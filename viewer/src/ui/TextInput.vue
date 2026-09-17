<script setup lang="ts">
const value = defineModel<string>({ default: "" });
withDefaults(defineProps<{ type?: string; placeholder?: string; label?: string }>(), { type: "text" });
</script>

<template>
  <input v-model="value" :type="type" :placeholder="placeholder" :aria-label="label ?? placeholder" class="input">
</template>

<style scoped>
.input {
  width: 100%;
  height: var(--control-height);
  padding: 0 var(--space-3);
  border: 1px solid var(--border);
  border-radius: var(--radius-control);
  background: var(--surface-2);
  color: var(--text);
  font-size: var(--text-meta);
}
.input:hover { border-color: var(--border-strong); }
.input:focus { outline: 0; border-color: var(--blueprint); box-shadow: 0 0 0 3px var(--blueprint-soft); }
.input::placeholder { color: var(--dim); }
.input[type="search"]::-webkit-search-cancel-button { appearance: none; }
</style>
