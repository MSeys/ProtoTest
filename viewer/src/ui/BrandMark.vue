<script setup lang="ts">
// One asset, inlined, so the mark follows the surface instantly instead of swapping two files.
// The geometry is the approved one from the design handoff; only the fills are the surface's.
import markup from "../../../assets/brand/prototest-mark.svg?raw";

withDefaults(defineProps<{ size?: number }>(), { size: 34 });
</script>

<template>
  <span class="mark" :style="{ width: `${size}px`, height: `${size}px` }" role="img" aria-label="ProtoTest" v-html="markup" />
</template>

<style scoped>
.mark { flex: none; display: inline-block; }
.mark :deep(svg) { width: 100%; height: 100%; display: block; overflow: visible; }
.mark :deep(path:nth-of-type(1)) { fill: var(--brand-mark-body); }
.mark :deep(path:nth-of-type(2)) { fill: var(--brand-mark-check); }
</style>
