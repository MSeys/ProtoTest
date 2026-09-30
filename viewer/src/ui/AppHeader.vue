<script setup lang="ts">
import { ref } from "vue";
import AppButton from "./AppButton.vue";
import Icon from "./Icon.vue";
import BrandMark from "./BrandMark.vue";
import { copyText } from "../share";

/** One step of the path: the run, a test, the operation or item in the details. */
export interface Crumb {
  label: string;
  href: string;
  /** The full name when the label is shortened. */
  title?: string;
}

const props = defineProps<{
  share?: string | null;
  /** Where the reader is, from the run down; empty on the start screen. */
  trail?: Crumb[];
  /** The test list toggle, when a trace is open. */
  rail?: { open: boolean };
}>();
defineEmits<{ open: []; toggleRail: [] }>();

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

// The button confirms briefly, so sharing from a trace opened out of a link is one click.
const copied = ref(false);
let copiedTimer: ReturnType<typeof setTimeout> | undefined;
async function copyShare() {
  if (!props.share || !await copyText(props.share)) return;
  copied.value = true;
  clearTimeout(copiedTimer);
  copiedTimer = setTimeout(() => { copied.value = false; }, 2000);
}
</script>

<template>
  <header class="topbar">
    <AppButton v-if="rail" variant="icon" class="rail-toggle" :label="rail.open ? 'Hide the test list' : 'Show the test list'"
               :aria-expanded="rail.open" @click="$emit('toggleRail')">
      <Icon name="sidebar" />
    </AppButton>
    <a class="brand" href="#/">
      <BrandMark :size="28" />
      <span class="wordmark"><strong>ProtoTrace</strong><small v-if="!trail?.length">Execution blueprint</small></span>
    </a>
    <nav v-if="trail?.length" class="trail" aria-label="Where you are">
      <template v-for="(crumb, index) in trail" :key="crumb.href + crumb.label">
        <span v-if="index" class="sep" aria-hidden="true">/</span>
        <a :href="crumb.href" :title="crumb.title ?? crumb.label" :aria-current="index === trail.length - 1 ? 'page' : undefined">{{ crumb.label }}</a>
      </template>
    </nav>
    <p v-else class="privacy"><span aria-hidden="true">◇</span> Trace stays in this browser</p>
    <div class="actions">
      <a class="docs" href="https://prototest.dev/docs/">Docs</a>
      <!-- Same order as the HTML report and the docs: the page's own action first, the theme switch last. -->
      <AppButton v-if="share" class="copy" aria-live="polite" @click="copyShare">{{ copied ? "Copied" : "Copy link" }}</AppButton>
      <AppButton variant="primary" @click="$emit('open')">Open trace</AppButton>
      <AppButton variant="icon" :label="`Use ${theme === 'dark' ? 'light' : 'dark'} mode`" @click="toggleTheme">
        <Icon :name="theme === 'dark' ? 'sun' : 'moon'" />
      </AppButton>
    </div>
  </header>
</template>

<style scoped>
/* One row that shrinks from the middle: the brand and the actions keep their size, the path gives way.
   Nothing can overlap, because nothing is placed in a column narrower than its content. */
.topbar {
  flex: none;
  min-height: 52px;
  padding: var(--space-2) clamp(10px, 1.4vw, 20px);
  display: flex;
  align-items: center;
  gap: var(--space-3);
  border-bottom: 1px solid var(--border);
  background: var(--surface);
  container-type: inline-size;
}
.brand { flex: none; display: flex; align-items: center; gap: var(--space-2); color: inherit; text-decoration: none; }
.wordmark { display: flex; flex-direction: column; line-height: var(--leading-tight); }
.wordmark strong { font-family: var(--font-display); font-size: var(--text-title); letter-spacing: .01em; white-space: nowrap; }
.wordmark small { color: var(--dim); font-size: var(--text-micro); letter-spacing: var(--tracking-eyebrow); text-transform: uppercase; white-space: nowrap; }
.trail { flex: 1 1 auto; min-width: 0; display: flex; align-items: center; gap: var(--space-1); overflow: hidden; font-size: var(--text-body); }
.trail a {
  flex: 0 1 auto;
  min-width: 0;
  padding: 2px var(--space-2);
  overflow: hidden;
  border-radius: var(--radius-chip);
  color: var(--muted);
  text-decoration: none;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.trail a:hover { background: var(--hover); color: var(--text); }
.trail a[aria-current="page"] { color: var(--text); font-weight: var(--weight-semibold); }
/* The first step is the file; it gives way before the test and the operation do. */
.trail a:first-child { flex-shrink: 4; }
.sep { flex: none; color: var(--dim); }
.privacy { flex: 1 1 auto; min-width: 0; overflow: hidden; color: var(--muted); font-size: var(--text-meta); text-align: center; text-overflow: ellipsis; white-space: nowrap; }
.privacy span { color: var(--blueprint); }
.actions { flex: none; margin-left: auto; display: flex; align-items: center; gap: var(--space-2); }
.docs { height: var(--control-height); padding: 0 var(--space-3); display: grid; place-items: center; border: 1px solid transparent; border-radius: var(--radius-control); color: var(--muted); font-size: var(--text-meta); text-decoration: none; white-space: nowrap; }
.docs:hover { border-color: var(--border); color: var(--text); }

@container (max-width: 760px) {
  .privacy, .wordmark small { display: none; }
  /* The path keeps the test and the operation; the file is one step up the list. */
  .trail a:first-child, .trail a:first-child + .sep { display: none; }
}
@container (max-width: 560px) {
  .wordmark, .docs { display: none; }
}
@container (max-width: 420px) {
  .copy { display: none; }
}
</style>
