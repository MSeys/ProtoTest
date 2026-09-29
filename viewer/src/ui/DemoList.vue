<script setup lang="ts">
import { computed } from "vue";
import { demos, formatDemoFacts, type DemoFacts } from "../demos";
import { tone } from "../trace/format";

/*
 * The bundled demos as two groups: the product runs as cards, each drawing the run's own outcome
 * shape from its trace, and the recipes as a quieter list under them. The facts and the strip come
 * from the trace itself; a failed read leaves the entry clickable without facts.
 */
const props = defineProps<{ facts: Record<string, DemoFacts | null> }>();
defineEmits<{ open: [key: string] }>();

const runs = computed(() => demos.filter(entry => entry.group === "run"));
const recipes = computed(() => demos.filter(entry => entry.group === "recipe"));

function factsOf(key: string): DemoFacts | null {
  return props.facts[key] ?? null;
}
function reading(key: string): boolean {
  return !(key in props.facts);
}
</script>

<template>
  <div class="demo-groups">
    <ul class="demo-runs">
      <li v-for="entry in runs" :key="entry.key">
        <button type="button" class="demo-card" @click.stop="$emit('open', entry.key)" @keydown.stop>
          <span class="demo-head">
            <strong>{{ entry.label }}</strong>
            <small v-if="factsOf(entry.key)" class="demo-facts">{{ formatDemoFacts(factsOf(entry.key)!) }}</small>
            <small v-else-if="reading(entry.key)" class="demo-facts">reading…</small>
          </span>
          <span v-if="factsOf(entry.key)" class="demo-strip" aria-hidden="true">
            <i v-for="(outcome, index) in factsOf(entry.key)!.outcomes" :key="index" :class="tone(outcome)" />
          </span>
          <small class="demo-description">{{ entry.description }}</small>
        </button>
      </li>
    </ul>

    <section class="demo-recipes" aria-label="Recipe demos">
      <h3>Recipes</h3>
      <ul>
        <li v-for="entry in recipes" :key="entry.key">
          <button type="button" class="demo-row" @click.stop="$emit('open', entry.key)" @keydown.stop>
            <span class="demo-head">
              <strong>{{ entry.label }}</strong>
              <small v-if="factsOf(entry.key)" class="demo-facts">{{ formatDemoFacts(factsOf(entry.key)!) }}</small>
              <small v-else-if="reading(entry.key)" class="demo-facts">reading…</small>
            </span>
            <small class="demo-description">{{ entry.description }}</small>
          </button>
        </li>
      </ul>
    </section>
  </div>
</template>

<style scoped>
.demo-groups { display: grid; gap: var(--space-4); }

.demo-runs { margin: 0; padding: 0; display: grid; gap: var(--space-2); list-style: none; }

.demo-card {
  width: 100%;
  padding: var(--space-3) var(--space-4);
  display: grid;
  gap: var(--space-2);
  border: 1px solid var(--border);
  border-radius: var(--radius-panel);
  background: var(--surface);
  color: var(--text);
  text-align: left;
  cursor: pointer;
}
.demo-card:hover, .demo-card:focus-visible { border-color: var(--blueprint); }

.demo-head { display: flex; flex-wrap: wrap; align-items: baseline; justify-content: space-between; gap: var(--space-1) var(--space-3); }
.demo-head strong { font-size: var(--text-strong); font-weight: var(--weight-semibold); }
.demo-facts { color: var(--dim); font: var(--text-micro) var(--font-mono); }

/* One tick per test in start order: the run view's own shape language, at a smaller scale. */
.demo-strip { display: flex; flex-wrap: wrap; align-items: flex-end; align-content: flex-start; gap: 2px; min-height: 12px; }
.demo-strip i { flex: none; width: 3px; height: 5px; border-radius: var(--radius-hairline); background: var(--outcome-succeeded); opacity: 0.45; }
.demo-strip i.danger, .demo-strip i.warning { height: 12px; }
.demo-strip i.danger { background: var(--outcome-failed); opacity: 1; }
.demo-strip i.warning { background: var(--outcome-partial); opacity: 1; }
.demo-strip i.neutral { background: transparent; box-shadow: inset 0 0 0 1px var(--border-strong); opacity: 1; }

.demo-description { color: var(--muted); font-size: var(--text-micro); overflow-wrap: anywhere; }

.demo-recipes { display: grid; gap: var(--space-2); }
.demo-recipes h3 { font-family: var(--font-ui); font-size: var(--text-meta); font-weight: var(--weight-bold); color: var(--muted); }
.demo-recipes ul { margin: 0; padding: 0; display: grid; list-style: none; border: 1px solid var(--border); border-radius: var(--radius-control); background: var(--surface); }
.demo-row { width: 100%; padding: var(--space-2) var(--space-3); display: grid; gap: 2px; border: 0; border-top: 1px solid var(--border); background: transparent; color: var(--text); text-align: left; cursor: pointer; }
.demo-recipes li:first-child .demo-row { border-top: 0; }
.demo-row:hover, .demo-row:focus-visible { background: var(--hover); }
</style>
