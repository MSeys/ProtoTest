<template>
  <section class="not-found" data-testid="not-found">
    <p class="not-found__code mono">404</p>
    <h1>That page is not part of the console</h1>
    <p class="muted">
      The address does not match any Northstar screen. Check the link, or head back to the dashboard.
    </p>
    <RouterLink class="button" to="/console/" data-testid="not-found-home">Back to dashboard</RouterLink>
  </section>
</template>

<style scoped>
.not-found {
  display: grid;
  justify-items: start;
  gap: 10px;
  padding-top: clamp(24px, 10vh, 96px);
  max-width: 52ch;
}

.not-found__code {
  color: var(--ns-faint);
  font-size: 13px;
  letter-spacing: 0.08em;
}

.not-found .button {
  margin-top: 8px;
}
</style>
