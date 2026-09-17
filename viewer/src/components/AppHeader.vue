<script setup lang="ts">
import { ref, computed } from "vue";
import logoLight from "../../../assets/brand/prototest-mark.svg?url";
import logoDark from "../../../assets/brand/prototest-mark-white.svg?url";

defineEmits<{ open: [] }>();
type Theme = "light" | "dark";
let savedTheme: Theme | null = null;
try { savedTheme = localStorage.getItem("prototest-trace-theme") as Theme | null; } catch { /* Storage may be disabled. */ }
const theme = ref<Theme>(savedTheme ?? (matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light"));
const logoUrl = computed(() => theme.value === "dark" ? logoDark : logoLight);
document.documentElement.dataset.theme = theme.value;
function toggleTheme() {
  theme.value = theme.value === "dark" ? "light" : "dark";
  document.documentElement.dataset.theme = theme.value;
  try { localStorage.setItem("prototest-trace-theme", theme.value); } catch { /* The selected theme still applies to this page. */ }
}
</script>

<template>
  <header class="topbar">
    <div class="brand">
      <img :src="logoUrl" alt="">
      <span><strong>ProtoTrace</strong><small>Execution blueprint</small></span>
    </div>
    <div class="privacy"><span aria-hidden="true">◇</span> Trace stays in this browser</div>
    <div class="top-actions">
      <button class="icon-button" type="button" :title="`Use ${theme === 'dark' ? 'light' : 'dark'} mode`" aria-label="Toggle color theme" @click="toggleTheme">{{ theme === 'dark' ? '☀' : '◐' }}</button>
      <button class="primary" type="button" @click="$emit('open')">Open trace</button>
    </div>
  </header>
</template>
