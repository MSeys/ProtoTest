<script setup lang="ts">
/**
 * The handful of line icons the viewer draws. They are strokes in currentColor on a 16px grid, so they take
 * the colour and hover state of whatever button holds them. The HTML report draws the same paths.
 */
defineProps<{ name: "sun" | "moon" | "sidebar" }>();
</script>

<template>
  <svg class="icon" viewBox="0 0 16 16" width="16" height="16" aria-hidden="true"
       fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
    <template v-if="name === 'sun'">
      <circle cx="8" cy="8" r="3" />
      <path d="M8 1.5v1.6M8 12.9v1.6M1.5 8h1.6M12.9 8h1.6M3.4 3.4l1.1 1.1M11.5 11.5l1.1 1.1M3.4 12.6l1.1-1.1M11.5 4.5l1.1-1.1" />
    </template>
    <path v-else-if="name === 'moon'" d="M13.2 10.2A5.5 5.5 0 0 1 5.8 2.8a5.5 5.5 0 1 0 7.4 7.4Z" />
    <template v-else>
      <rect x="2" y="3" width="12" height="10" rx="1.5" />
      <path d="M6 3v10" />
    </template>
  </svg>
</template>

<style scoped>
.icon { display: block; flex: none; }
</style>
