<script setup lang="ts">
import { ref } from "vue";
import AppButton from "./AppButton.vue";
import BrandMark from "./BrandMark.vue";

defineEmits<{ open: [] }>();
type Theme = "light" | "dark";
let savedTheme: Theme | null = null;
try { savedTheme = localStorage.getItem("prototest-trace-theme") as Theme | null; } catch { /* Storage may be disabled. */ }
// The ProtoTrace panel is the dark execution surface by design; the paper variant is an explicit choice.
const theme = ref<Theme>(savedTheme ?? "dark");
document.documentElement.dataset.theme = theme.value;
function toggleTheme() {
  theme.value = theme.value === "dark" ? "light" : "dark";
  document.documentElement.dataset.theme = theme.value;
  try { localStorage.setItem("prototest-trace-theme", theme.value); } catch { /* The selected theme still applies to this page. */ }
}
</script>

<template>
  <header class="topbar">
    <a class="brand" href="#/">
      <BrandMark :size="32" />
      <span><strong>ProtoTrace</strong><small>Execution blueprint</small></span>
    </a>
    <p class="privacy"><span aria-hidden="true">◇</span> Trace stays in this browser</p>
    <div class="actions">
      <AppButton variant="icon" :label="`Use ${theme === 'dark' ? 'light' : 'dark'} mode`" @click="toggleTheme">{{ theme === 'dark' ? '☀' : '◐' }}</AppButton>
      <AppButton variant="primary" @click="$emit('open')">Open trace</AppButton>
    </div>
  </header>
</template>

<style scoped>
.topbar {
  flex: none;
  min-height: 56px;
  padding: var(--space-3) clamp(12px, 2vw, 24px);
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto minmax(0, 1fr);
  align-items: center;
  gap: var(--space-4);
  border-bottom: 1px solid var(--border);
  background: var(--surface);
  container-type: inline-size;
}
.brand { min-width: 0; display: flex; align-items: center; gap: var(--space-3); color: inherit; text-decoration: none; }
.brand > span { min-width: 0; display: flex; flex-direction: column; line-height: var(--leading-tight); }
.brand strong { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-family: var(--font-display); font-size: var(--text-title); letter-spacing: .01em; }
.brand small { color: var(--dim); font-size: var(--text-micro); letter-spacing: var(--tracking-eyebrow); text-transform: uppercase; }
.privacy { justify-self: center; color: var(--muted); font-size: var(--text-meta); white-space: nowrap; }
.privacy span { color: var(--success); }
.actions { justify-self: end; display: flex; align-items: center; gap: var(--space-3); }

@container (max-width: 760px) {
  .topbar { grid-template-columns: minmax(0, 1fr) auto; }
  .privacy { display: none; }
  .brand small { display: none; }
}
</style>
