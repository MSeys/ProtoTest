<script setup lang="ts">
import { computed, ref } from "vue";

const props = defineProps<{
  items: { id: string; label: string; href?: string }[];
  active: string;
  variant?: "underline" | "pill";
  /** The accessible name of the tab list. */
  label?: string;
  /** The id of the panel the tabs control; each tab id becomes `<panel>-tab-<item.id>`. */
  panel?: string;
}>();
const emit = defineEmits<{ select: [id: string] }>();

const list = ref<HTMLElement>();
// Underline tabs are the route strip: a real tab list with roving focus and a panel. A pill row only
// filters something in place, so it reads as a group of pressed buttons instead.
const isTabs = computed(() => (props.variant ?? "underline") !== "pill");

function move(event: KeyboardEvent) {
  if (!isTabs.value) return;
  if (event.key !== "ArrowLeft" && event.key !== "ArrowRight" && event.key !== "Home" && event.key !== "End") return;
  const index = Math.max(0, props.items.findIndex(item => item.id === props.active));
  const next = event.key === "ArrowLeft" ? (index - 1 + props.items.length) % props.items.length
    : event.key === "ArrowRight" ? (index + 1) % props.items.length
      : event.key === "Home" ? 0 : props.items.length - 1;
  const target = props.items[next];
  if (!target) return;
  event.preventDefault();
  list.value?.querySelectorAll<HTMLElement>("[role='tab']")[next]?.focus();
  emit("select", target.id);
}
</script>

<template>
  <div ref="list" class="tabs" :class="variant ?? 'underline'"
       :role="isTabs ? 'tablist' : 'group'" :aria-label="label" @keydown="move">
    <template v-for="item in items" :key="item.id">
      <a v-if="item.href" :id="panel ? `${panel}-tab-${item.id}` : undefined" :href="item.href"
         :role="isTabs ? 'tab' : undefined" :aria-selected="isTabs ? item.id === active : undefined"
         :aria-controls="isTabs ? panel : undefined" :tabindex="isTabs ? (item.id === active ? 0 : -1) : undefined"
         :class="{ active: item.id === active }">{{ item.label }}</a>
      <button v-else type="button" :id="panel ? `${panel}-tab-${item.id}` : undefined"
              :role="isTabs ? 'tab' : undefined" :aria-selected="isTabs ? item.id === active : undefined"
              :aria-pressed="isTabs ? undefined : item.id === active"
              :aria-controls="isTabs ? panel : undefined" :tabindex="isTabs ? (item.id === active ? 0 : -1) : undefined"
              :class="{ active: item.id === active }" @click="emit('select', item.id)">{{ item.label }}</button>
    </template>
  </div>
</template>

<style scoped>
/* Underline navigates between views; pill filters a list. One component, so they cannot drift apart. */
.tabs { display: flex; align-items: center; gap: 2px; overflow-x: auto; }
.tabs > a, .tabs > button {
  flex: none;
  border: 0;
  background: transparent;
  color: var(--muted);
  font-size: var(--text-meta);
  text-decoration: none;
  transition: color var(--motion-fast) var(--motion-ease), background var(--motion-fast) var(--motion-ease);
}
.underline > a, .underline > button {
  height: 32px;
  padding: 0 var(--space-4);
  display: inline-flex;
  align-items: center;
  border-bottom: 2px solid transparent;
  border-radius: var(--radius-chip) var(--radius-chip) 0 0;
}
.underline > a:hover, .underline > button:hover { background: var(--hover); color: var(--text); }
.underline > .active { border-bottom-color: var(--blueprint); color: var(--text); font-weight: var(--weight-bold); }
.pill { gap: var(--space-1); }
.pill > a, .pill > button {
  height: 24px;
  padding: 0 var(--space-4);
  display: inline-flex;
  align-items: center;
  border: 1px solid var(--border);
  border-radius: var(--radius-pill);
  font-size: var(--text-micro);
}
.pill > a:hover, .pill > button:hover { border-color: var(--border-strong); color: var(--text); }
.pill > .active { border-color: var(--blueprint); background: var(--blueprint-soft); color: var(--text); font-weight: var(--weight-bold); }
</style>
