<script setup lang="ts">
import { useRouter } from "vue-router";
import StatusBadge from "./components/StatusBadge.vue";
import ToastHost from "./components/ToastHost.vue";
import { session } from "./session";

const router = useRouter();

async function signOut(): Promise<void> {
  await session.signOut();
  await router.push({ name: "signin" });
}
</script>

<template>
  <div class="shell">
    <header class="topbar">
      <RouterLink class="brand" to="/console/" data-testid="brand">
        <svg class="brand__mark" viewBox="0 0 24 24" aria-hidden="true">
          <path d="M12 1.6 14.6 9.4 22.4 12 14.6 14.6 12 22.4 9.4 14.6 1.6 12 9.4 9.4Z" />
        </svg>
        <span class="brand__name">Northstar</span>
        <span class="brand__product">Console</span>
      </RouterLink>

      <div v-if="session.state.organization" class="topbar__session">
        <span class="topbar__organization" data-testid="session-organization">
          {{ session.state.organization.name }}
        </span>
        <StatusBadge
          :status="session.state.organization.status"
          :label="session.state.organization.planName"
          testid="session-plan"
        />
        <button class="button button--quiet topbar__signout" type="button" data-testid="sign-out" @click="signOut">
          Sign out
        </button>
      </div>
    </header>

    <div class="shell__body">
      <nav v-if="session.state.organization" class="sidenav" aria-label="Primary">
        <RouterLink class="sidenav__link" to="/console/" data-testid="nav-dashboard">Dashboard</RouterLink>
        <RouterLink class="sidenav__link" to="/console/projects" data-testid="nav-projects">Projects</RouterLink>
        <RouterLink class="sidenav__link" to="/console/billing" data-testid="nav-billing">Billing</RouterLink>
        <RouterLink class="sidenav__link" to="/console/reports" data-testid="nav-reports">Reports</RouterLink>
      </nav>

      <main class="content">
        <RouterView />
      </main>
    </div>

    <ToastHost />
  </div>
</template>

<style scoped>
.shell {
  min-height: 100vh;
  display: flex;
  flex-direction: column;
}

.topbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 10px 20px;
  min-height: 52px;
  padding: 8px 22px;
  background: var(--ns-ink-deep);
  color: #e7efed;
}

.brand {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  color: inherit;
}

.brand:hover {
  text-decoration: none;
}

.brand__mark {
  width: 20px;
  height: 20px;
  fill: #6d7ded;
}

.brand__name {
  font-family: var(--font-display);
  font-size: 17px;
  font-weight: 600;
  letter-spacing: -0.01em;
}

.brand__product {
  border: 1px solid rgb(231 239 237 / 25%);
  border-radius: 999px;
  padding: 1px 8px;
  color: #a9bdb8;
  font-size: 11.5px;
}

.topbar__session {
  display: flex;
  align-items: center;
  gap: 12px;
}

.topbar__organization {
  color: #c5d4d0;
  font-size: 13.5px;
}

.topbar__signout {
  border-color: rgb(231 239 237 / 25%);
  background: transparent;
  color: #e7efed;
}

.topbar__signout:hover:not(:disabled) {
  border-color: rgb(231 239 237 / 55%);
  background: rgb(231 239 237 / 8%);
}

.shell__body {
  flex: 1;
  display: flex;
  flex-wrap: wrap;
  align-items: flex-start;
  align-content: flex-start;
}

.sidenav {
  flex: 0 0 196px;
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 22px 12px;
}

.sidenav__link {
  border-left: 3px solid transparent;
  border-radius: 0 7px 7px 0;
  padding: 8px 12px;
  color: var(--ns-muted);
  font-size: 14px;
  font-weight: 600;
}

.sidenav__link:hover {
  background: #e6ecea;
  color: var(--ns-ink);
  text-decoration: none;
}

.sidenav__link[aria-current="page"] {
  border-left-color: var(--ns-accent);
  background: var(--ns-surface);
  color: var(--ns-ink);
}

.content {
  flex: 1 1 560px;
  min-width: 0;
  padding: 26px 28px 72px;
}

/* Below the point where the content column wraps, the nav becomes a horizontal strip: the same links,
   a shape that costs one row instead of a column. */
@media (max-width: 760px) {
  .sidenav {
    flex: 1 1 100%;
    flex-direction: row;
    overflow-x: auto;
    padding: 10px 16px 0;
  }

  .sidenav__link {
    border-left: 0;
    border-bottom: 3px solid transparent;
    border-radius: 7px 7px 0 0;
    white-space: nowrap;
  }

  .sidenav__link[aria-current="page"] {
    border-bottom-color: var(--ns-accent);
  }

  .content {
    padding: 20px 18px 64px;
  }
}
</style>
