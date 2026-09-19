<script setup lang="ts">
import { dismiss, toasts } from "../toasts";
</script>

<template>
  <div class="toast-host" aria-live="polite">
    <div
      v-for="item in toasts"
      :key="item.id"
      class="toast"
      :data-tone="item.tone"
      :role="item.tone === 'error' ? 'alert' : 'status'"
      data-testid="toast"
    >
      <span class="toast__message">{{ item.message }}</span>
      <button class="toast__close" type="button" aria-label="Dismiss" @click="dismiss(item.id)">×</button>
    </div>
  </div>
</template>

<style scoped>
.toast-host {
  position: fixed;
  right: 18px;
  bottom: 18px;
  z-index: 50;
  display: grid;
  gap: 8px;
  max-width: min(360px, calc(100vw - 36px));
}

.toast {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  border: 1px solid var(--ns-line);
  border-left: 3px solid var(--ns-neutral);
  border-radius: var(--ns-radius-control);
  background: var(--ns-surface);
  box-shadow: 0 6px 18px rgb(16 27 35 / 12%);
  padding: 10px 12px;
  font-size: 14px;
}

.toast[data-tone="success"] {
  border-left-color: var(--ns-success);
}

.toast[data-tone="error"] {
  border-left-color: var(--ns-danger);
}

.toast__message {
  flex: 1;
}

.toast__close {
  border: 0;
  background: none;
  color: var(--ns-muted);
  font-size: 16px;
  line-height: 1;
  cursor: pointer;
  padding: 0 2px;
}
</style>
