<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import {
  ApiProblem,
  getOrganization,
  graphql,
  listProjects,
  type Deployment,
  type Organization,
  type Project,
  type Subscription,
  type UsageSummary
} from "../api";
import LoadingBlock from "../components/LoadingBlock.vue";
import PageHeader from "../components/PageHeader.vue";
import StatusBadge from "../components/StatusBadge.vue";
import { formatDate, formatDateTime, formatQuantity, shortSha } from "../format";

interface DashboardGraph {
  apiInfo: { name: string; version: string };
  subscription: Subscription;
  usageSummary: UsageSummary;
  deployments: { totalCount: number; nodes: Deployment[] };
}

const DASHBOARD_QUERY = `query ConsoleDashboard {
  apiInfo { name version }
  subscription { planName status seats includedSeats includedDeployMinutes currentPeriodEndUtc }
  usageSummary(metric: "deploy_minutes") { total included overage fromUtc toUtc }
  deployments(first: 5) { totalCount nodes { id projectId environmentId version commitSha status completedAtUtc } }
}`;

const organization = ref<Organization | null>(null);
const projects = ref<Project[]>([]);
const graph = ref<DashboardGraph | null>(null);
const loading = ref(true);
const error = ref<string | null>(null);

const projectNames = computed(() => {
  const names = new Map<string, string>();
  for (const project of projects.value) {
    names.set(project.id, project.name);
  }
  return names;
});

const allowance = computed(() => {
  const usage = graph.value?.usageSummary;
  if (!usage || usage.included <= 0) {
    return { used: 0, overage: 0, includedPercent: 0, overagePercent: 0 };
  }
  const used = Math.min(usage.total, usage.included);
  const scale = Math.max(usage.included, usage.total);
  return {
    used: usage.total,
    overage: usage.overage,
    includedPercent: (used / scale) * 100,
    overagePercent: (usage.overage / scale) * 100
  };
});

async function load(): Promise<void> {
  loading.value = true;
  error.value = null;
  try {
    const [restOrganization, restProjects, graphqlData] = await Promise.all([
      getOrganization(),
      listProjects(100),
      graphql<DashboardGraph>(DASHBOARD_QUERY)
    ]);
    organization.value = restOrganization;
    projects.value = restProjects.items;
    graph.value = graphqlData;
  } catch (cause) {
    error.value = cause instanceof ApiProblem ? cause.message : "The dashboard could not be loaded.";
  } finally {
    loading.value = false;
  }
}

onMounted(load);
</script>

<template>
  <section data-testid="dashboard-page">
    <PageHeader
      title="Dashboard"
      description="The plan, the projects and the minutes this organization has spent deploying."
      testid="dashboard-title"
    >
      <template #actions>
        <button class="button" type="button" data-testid="dashboard-refresh" :disabled="loading" @click="load">
          Refresh
        </button>
      </template>
    </PageHeader>

    <p v-if="error" class="notice dashboard-error" data-tone="danger" data-testid="dashboard-error">
      {{ error }}
    </p>

    <div class="stats">
      <article class="stat panel">
        <p class="stat__label">Plan</p>
        <p class="stat__value" data-testid="stat-plan">{{ organization?.planName ?? "—" }}</p>
        <p class="stat__meta">
          <StatusBadge v-if="organization" :status="organization.status" testid="stat-plan-status" />
          <span v-else class="muted">Waiting for the API</span>
        </p>
      </article>

      <article class="stat panel">
        <p class="stat__label">Projects</p>
        <p class="stat__value" data-testid="stat-projects">{{ organization?.projectCount ?? projects.length }}</p>
        <p class="stat__meta muted" data-testid="stat-projects-limit">
          {{ organization?.projectLimit ? `of ${organization.projectLimit} on this plan` : "unlimited on this plan" }}
        </p>
      </article>

      <article class="stat panel">
        <p class="stat__label">Deploy minutes</p>
        <p class="stat__value" data-testid="stat-deploy-minutes">
          {{ graph ? formatQuantity(graph.usageSummary.total) : "—" }}
        </p>
        <p class="stat__meta muted" data-testid="stat-deploy-included">
          {{ graph ? `${formatQuantity(graph.usageSummary.included)} included` : "this billing period" }}
        </p>
      </article>

      <article class="stat panel">
        <p class="stat__label">Current period ends</p>
        <p class="stat__value stat__value--date" data-testid="stat-period-end">
          {{ organization ? formatDate(organization.currentPeriodEndUtc) : "—" }}
        </p>
        <p class="stat__meta muted">
          {{ organization?.cancelAtPeriodEnd ? "Cancels at period end" : "Renews automatically" }}
        </p>
      </article>
    </div>

    <div class="grid">
      <section class="panel">
        <div class="panel__header">
          <h2>Plan allowance</h2>
          <span class="source-tag" data-testid="source-graphql">GraphQL</span>
        </div>
        <LoadingBlock v-if="loading" :rows="2" label="Loading allowance" />
        <div v-else-if="graph" class="panel__body allowance" data-testid="allowance">
          <div
            class="allowance__meter"
            role="meter"
            aria-label="Deploy minutes used against the plan allowance"
            :aria-valuemin="0"
            :aria-valuemax="graph.usageSummary.included"
            :aria-valuenow="graph.usageSummary.total"
            :aria-valuetext="`${formatQuantity(graph.usageSummary.total)} of ${formatQuantity(graph.usageSummary.included)} deploy minutes`"
          >
            <span class="allowance__fill" :style="{ width: `${allowance.includedPercent}%` }" />
            <span
              class="allowance__overage"
              :style="{ width: `${allowance.overagePercent}%`, left: `${allowance.includedPercent}%` }"
            />
          </div>
          <dl class="allowance__facts">
            <div>
              <dt>Used</dt>
              <dd data-testid="allowance-used">{{ formatQuantity(graph.usageSummary.total) }} min</dd>
            </div>
            <div>
              <dt>Included</dt>
              <dd data-testid="allowance-included">{{ formatQuantity(graph.usageSummary.included) }} min</dd>
            </div>
            <div>
              <dt>Overage</dt>
              <dd data-testid="allowance-overage">{{ formatQuantity(graph.usageSummary.overage) }} min</dd>
            </div>
          </dl>
          <p class="muted allowance__note">
            {{ graph.subscription.planName }} · {{ graph.subscription.seats }}
            {{ graph.subscription.seats === 1 ? "seat" : "seats" }} · billing period ends
            {{ formatDate(graph.subscription.currentPeriodEndUtc) }}
          </p>
        </div>
      </section>

      <section class="panel">
        <div class="panel__header">
          <h2>Latest releases</h2>
          <span class="source-tag" data-testid="source-graphql-releases">GraphQL</span>
        </div>
        <LoadingBlock v-if="loading" :rows="3" label="Loading releases" />
        <ul v-else-if="graph && graph.deployments.nodes.length" class="releases" data-testid="recent-deployments">
          <li
            v-for="deployment in graph.deployments.nodes"
            :key="deployment.id"
            class="release"
            data-testid="recent-deployment"
          >
            <div class="release__main">
              <span class="mono release__version">{{ deployment.version }}</span>
              <span class="mono muted">{{ shortSha(deployment.commitSha) }}</span>
            </div>
            <div class="release__meta">
              <RouterLink
                class="release__project"
                :to="`/console/projects/${deployment.projectId}`"
                data-testid="recent-deployment-project"
              >
                {{ projectNames.get(deployment.projectId) ?? deployment.projectId }}
              </RouterLink>
              <StatusBadge :status="deployment.status" testid="recent-deployment-status" />
              <time class="muted" :datetime="deployment.completedAtUtc ?? deployment.createdAtUtc">
                {{ formatDateTime(deployment.completedAtUtc ?? deployment.createdAtUtc) }}
              </time>
            </div>
          </li>
        </ul>
        <p v-else class="panel__body muted" data-testid="recent-deployments-empty">
          Nothing has been deployed yet. Open a project to create its first environment and release.
        </p>
      </section>
    </div>
  </section>
</template>

<style scoped>
.dashboard-error {
  margin-bottom: 16px;
}

.stats {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 190px), 1fr));
  gap: 14px;
  margin-bottom: 18px;
}

.stat {
  padding: 14px 16px;
}

.stat__label {
  color: var(--ns-muted);
  font-size: 13px;
  font-weight: 600;
}

.stat__value {
  margin-top: 2px;
  font-family: var(--font-display);
  font-size: 27px;
  font-weight: 600;
  letter-spacing: -0.02em;
}

.stat__value--date {
  font-size: 21px;
  padding-top: 4px;
}

.stat__meta {
  margin-top: 5px;
  font-size: 13px;
}

.grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 400px), 1fr));
  gap: 14px;
  align-items: start;
}

.allowance {
  display: grid;
  gap: 14px;
}

.allowance__meter {
  position: relative;
  height: 10px;
  border-radius: 999px;
  background: var(--ns-line-soft);
  overflow: hidden;
}

.allowance__fill {
  position: absolute;
  inset: 0 auto 0 0;
  background: var(--ns-accent);
}

.allowance__overage {
  position: absolute;
  top: 0;
  bottom: 0;
  background: var(--ns-warning);
}

.allowance__facts {
  display: flex;
  flex-wrap: wrap;
  gap: 10px 28px;
  margin: 0;
}

.allowance__facts dt {
  color: var(--ns-muted);
  font-size: 12.5px;
  font-weight: 600;
}

.allowance__facts dd {
  margin: 2px 0 0;
  font-family: var(--font-mono);
  font-size: 14px;
}

.allowance__note {
  font-size: 13px;
}

.releases {
  margin: 0;
  padding: 0;
  list-style: none;
}

.release {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 6px 16px;
  padding: 12px 18px;
  border-bottom: 1px solid var(--ns-line-soft);
}

.release:last-child {
  border-bottom: 0;
}

.release__main {
  display: flex;
  align-items: baseline;
  gap: 10px;
}

.release__version {
  font-size: 14px;
  font-weight: 500;
}

.release__meta {
  display: flex;
  align-items: center;
  gap: 12px;
  font-size: 13px;
}
</style>
