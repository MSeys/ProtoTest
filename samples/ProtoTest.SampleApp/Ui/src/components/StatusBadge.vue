<script setup lang="ts">
import { statusLabel, statusTone } from "../status";

const props = defineProps<{
  status: string;
  label?: string;
  testid?: string;
}>();
</script>

<template>
  <span class="badge" :data-tone="statusTone(props.status)" :data-testid="props.testid" :data-status="props.status">
    {{ props.label ?? statusLabel(props.status) }}
  </span>
</template>
