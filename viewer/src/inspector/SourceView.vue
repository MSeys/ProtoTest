<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { fileName, readSource, type SourceLocation } from "../trace/sources";
import { highlightLines, languageOf, type HighlightedPart } from "../trace/highlight";

const props = withDefaults(defineProps<{ location: SourceLocation; context?: number }>(), { context: 4 });

const text = ref<string>();
watch(() => props.location.file, async file => {
  text.value = undefined;
  const content = await readSource(file);
  if (props.location.file === file) text.value = content;
}, { immediate: true });

// The whole file is tokenised once, so a comment or string that opens above the window still reads right.
const highlighted = computed(() => text.value === undefined
  ? []
  : highlightLines(text.value.replace(/\r\n/g, "\n"), languageOf(props.location.file)));

// The recorded line with a few lines either side; the file stays in the trace, so a wider view is a later step.
const excerpt = computed(() => {
  const all = highlighted.value;
  const first = Math.max(1, props.location.line - props.context);
  const last = Math.min(all.length, props.location.line + props.context);
  return { first, parts: all.slice(first - 1, last) };
});

// Strip the shared indentation so a method deep in a class still reads from the left edge.
const indent = computed(() => {
  const widths = excerpt.value.parts
    .map(parts => parts.map(part => part.text).join(""))
    .filter(line => line.trim())
    .map(line => line.length - line.trimStart().length);
  return widths.length ? Math.min(...widths) : 0;
});

const lines = computed(() => excerpt.value.parts.map((parts, index) => ({
  number: excerpt.value.first + index,
  parts: dedent(parts, indent.value)
})));

function dedent(parts: HighlightedPart[], width: number): HighlightedPart[] {
  let remaining = width;
  return parts.map(part => {
    if (remaining <= 0) return part;
    const leading = part.text.length - part.text.trimStart().length;
    const cut = Math.min(remaining, leading);
    remaining = cut < part.text.length ? 0 : remaining - cut;
    return { ...part, text: part.text.slice(cut) };
  }).filter(part => part.text);
}

// The documentation's code theme (docs/src/prism/prototest.ts): the innermost type decides, and a C# attribute
// (tokenised as attribute class-name) takes the annotation colour, as it does there.
const roles: Record<string, string> = {
  comment: "comment", prolog: "comment", doctype: "comment", cdata: "comment",
  keyword: "keyword", builtin: "keyword", important: "keyword", atrule: "keyword",
  "class-name": "type", "maybe-class-name": "type", "type-definition": "type", namespace: "namespace",
  function: "function", method: "function",
  string: "string", char: "string", regex: "string", url: "string", inserted: "string",
  number: "number", boolean: "number", constant: "number", symbol: "number",
  "attr-name": "attribute", annotation: "attribute", attribute: "attribute", property: "attribute", tag: "attribute",
  punctuation: "punctuation", operator: "punctuation",
  variable: "text", parameter: "text"
};

function role(types: string[]): string | undefined {
  if (types.includes("attribute") && types.includes("class-name")) return "annotation";
  let found: string | undefined;
  for (const type of types) found = roles[type] ?? found;
  return found;
}
</script>

<template>
  <section class="source">
    <h3>
      <span :title="location.file">{{ fileName(location.file) }}:{{ location.line }}</span>
      <small v-if="location.functionName">{{ location.functionName }}</small>
    </h3>
    <pre v-if="lines.length"><code><span v-for="line in lines" :key="line.number" class="line" :class="{ current: line.number === location.line }"><b>{{ line.number }}</b><template v-if="line.parts.length"><span v-for="(part, index) in line.parts" :key="index" :class="role(part.types)">{{ part.text }}</span></template><template v-else> </template>
</span></code></pre>
  </section>
</template>

<style scoped>
.source { min-width: 0; display: grid; gap: var(--space-2); }
h3 { display: flex; flex-wrap: wrap; align-items: baseline; gap: var(--space-1) var(--space-2); font-family: var(--font-mono); font-size: var(--text-meta); font-weight: var(--weight-bold); letter-spacing: 0; }
h3 small { min-width: 0; color: var(--dim); font-family: var(--font-ui); font-size: var(--text-micro); font-weight: var(--weight-regular); overflow-wrap: anywhere; }
pre { margin: 0; padding: var(--space-2) 0; overflow-x: auto; border-radius: var(--radius-control); background: var(--surface-sunken); color: var(--code-text); font: var(--text-micro)/var(--leading) var(--font-mono); }
.line { display: block; padding-right: var(--space-3); white-space: pre; }
.line b { display: inline-block; width: 4ch; margin-right: var(--space-3); color: var(--code-comment); font-weight: var(--weight-regular); text-align: right; }
.line.current { background: var(--code-highlight); box-shadow: inset 2px 0 0 var(--blueprint); }
.line.current b { color: var(--code-text); }
.comment { color: var(--code-comment); font-style: italic; }
.keyword { color: var(--code-keyword); }
.type { color: var(--code-type); }
.namespace { color: var(--code-namespace); }
.function { color: var(--code-function); }
.string { color: var(--code-string); }
.number { color: var(--code-number); }
.attribute { color: var(--code-attribute); }
.annotation { color: var(--code-annotation); }
.punctuation { color: var(--code-punctuation); }
</style>
