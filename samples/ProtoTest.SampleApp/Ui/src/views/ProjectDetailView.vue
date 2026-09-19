<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from "vue";
import {
  ApiProblem,
  createEnvironment,
  deploy,
  getProject,
  listDeployments,
  listEnvironments,
  rollback,
  type Deployment,
  type Environment,
  type Project
} from "../api";
import EmptyState from "../components/EmptyState.vue";
import LoadingBlock from "../components/LoadingBlock.vue";
import PageHeader from "../components/PageHeader.vue";
import StatusBadge from "../components/StatusBadge.vue";
import { formatDateTime, shortSha } from "../format";
import { subscribeToDeployments, type DeploymentSubscription, type LiveState } from "../realtime";
import { toast } from "../toasts";

const props = defineProps<{ projectId: string }>();

const project = ref<Project | null>(null);
const environments = ref<Environment[]>([]);
const deployments = ref<Deployment[]>([]);
const loading = ref(true);
const error = ref<string | null>(null);
const lastUpdated = ref<Date | null>(null);

const environmentId = ref("");
const version = ref("");
const commit = ref("");
const deployError = ref<string | null>(null);
const deploying = ref(false);

const envFormOpen = ref(false);
const envName = ref("");
const envKind = ref("preview");
const envError = ref<string | null>(null);
const creatingEnv = ref(false);

const promoting = ref<string | null>(null);
const rollingBack = ref<string | null>(null);

const liveState = ref<LiveState>("connecting");
const liveLabel = computed(() =>
  liveState.value === "live" ? "Live" : liveState.value === "connecting" ? "Connecting" : "Polling"
);

const environmentNames = computed(() => {
  const names = new Map<string, string>();
  for (const environment of environments.value) {
    names.set(environment.id, environment.name);
  }
  return names;
});

const production = computed(() => environments.value.find((environment) => environment.kind === "production") ?? null);

async function load(silent = false): Promise<void> {
  if (!silent) {
    loading.value = true;
  }
  error.value = null;
  try {
    const [restProject, restEnvironments, restDeployments] = await Promise.all([
      getProject(props.projectId),
      listEnvironments(props.projectId),
      listDeployments(props.projectId)
    ]);
    project.value = restProject;
    environments.value = restEnvironments.items;
    deployments.value = restDeployments.items;
    environmentId.value ||= restEnvironments.items[0]?.id ?? "";
    lastUpdated.value = new Date();
  } catch (cause) {
    if (!silent) {
      error.value = cause instanceof ApiProblem ? cause.message : "The project could not be loaded.";
    }
  } finally {
    if (!silent) {
      loading.value = false;
    }
  }
}

let subscription: DeploymentSubscription | null = null;
let pollTimer = 0;
let reloadTimer = 0;

function scheduleReload(): void {
  window.clearTimeout(reloadTimer);
  reloadTimer = window.setTimeout(() => void load(true), 200);
}

function syncPolling(): void {
  if (liveState.value === "live") {
    window.clearInterval(pollTimer);
    pollTimer = 0;
    return;
  }
  if (liveState.value === "polling" && !pollTimer) {
    pollTimer = window.setInterval(() => void load(true), 5000);
  }
}

onMounted(async () => {
  await load();
  subscription = subscribeToDeployments(
    (deployment) => {
      if (deployment.projectId === props.projectId) {
        scheduleReload();
      }
    },
    (state) => {
      liveState.value = state;
      syncPolling();
    }
  );
});

onBeforeUnmount(() => {
  subscription?.close();
  window.clearInterval(pollTimer);
  window.clearTimeout(reloadTimer);
});

function latestSuccessful(environment: Environment): Deployment | undefined {
  return deployments.value.find(
    (deployment) =>
      deployment.environmentId === environment.id &&
      deployment.status === "succeeded" &&
      deployment.version === environment.currentVersion
  );
}

function canRollback(deployment: Deployment): boolean {
  const environment = environments.value.find((item) => item.id === deployment.environmentId);
  if (
    deployment.status !== "succeeded" ||
    environment === undefined ||
    environment.currentVersion !== deployment.version
  ) {
    return false;
  }

  // The API rolls back to the previous successful deployment in the same environment; offer the action
  // only when that deployment exists, so the button never promises something the server would refuse.
  const completedAt = Date.parse(deployment.completedAtUtc ?? deployment.createdAtUtc);
  return deployments.value.some(
    (candidate) =>
      candidate.id !== deployment.id &&
      candidate.environmentId === deployment.environmentId &&
      candidate.status === "succeeded" &&
      Date.parse(candidate.completedAtUtc ?? candidate.createdAtUtc) < completedAt
  );
}

async function submitDeployment(): Promise<void> {
  const targetEnvironment = environmentId.value;
  const release = version.value.trim();
  const sha = commit.value.trim();
  if (!targetEnvironment) {
    deployError.value = "Choose an environment.";
    return;
  }
  if (!release) {
    deployError.value = "Enter a version.";
    return;
  }
  if (!sha) {
    deployError.value = "Enter the commit SHA being deployed.";
    return;
  }

  deploying.value = true;
  deployError.value = null;
  try {
    const deployment = await deploy(targetEnvironment, release, sha);
    toast(
      deployment.status === "failed" ? "error" : "success",
      deployment.status === "failed"
        ? `${release} failed to deploy.`
        : `${release} deployed to ${environmentNames.value.get(targetEnvironment) ?? "the environment"}.`
    );
    version.value = "";
    commit.value = "";
    await load(true);
  } catch (cause) {
    deployError.value = cause instanceof ApiProblem ? cause.message : "The deployment could not be started.";
  } finally {
    deploying.value = false;
  }
}

async function promote(environment: Environment): Promise<void> {
  const target = production.value;
  const source = latestSuccessful(environment);
  if (!target || !environment.currentVersion) {
    return;
  }
  if (!source) {
    toast("error", `No successful ${environment.currentVersion} deployment was found to promote.`);
    return;
  }

  promoting.value = environment.id;
  try {
    await deploy(target.id, environment.currentVersion, source.commitSha);
    toast("success", `${environment.currentVersion} promoted to ${target.name}.`);
    await load(true);
  } catch (cause) {
    toast("error", cause instanceof ApiProblem ? cause.message : "The promotion failed.");
  } finally {
    promoting.value = null;
  }
}

async function performRollback(deployment: Deployment): Promise<void> {
  rollingBack.value = deployment.id;
  try {
    await rollback(deployment.id);
    toast("success", `${deployment.version} rolled back.`);
    await load(true);
  } catch (cause) {
    toast("error", cause instanceof ApiProblem ? cause.message : "The rollback failed.");
  } finally {
    rollingBack.value = null;
  }
}

async function submitEnvironment(): Promise<void> {
  const name = envName.value.trim();
  if (!name) {
    envError.value = "Enter an environment name.";
    return;
  }

  creatingEnv.value = true;
  envError.value = null;
  try {
    const environment = await createEnvironment(props.projectId, name, envKind.value);
    toast("success", `${environment.name} created.`);
    envFormOpen.value = false;
    envName.value = "";
    await load(true);
  } catch (cause) {
    envError.value = cause instanceof ApiProblem ? cause.message : "The environment could not be created.";
  } finally {
    creatingEnv.value = false;
  }
}
</script>

<template>
  <section data-testid="project-detail">
    <PageHeader
      :title="project?.name ?? 'Project'"
      :description="project ? `Created ${formatDateTime(project.createdAtUtc)} · /${project.slug}` : undefined"
      testid="project-detail-name"
    >
      <template #actions>
        <span class="live" :data-state="liveState" data-testid="live-indicator">
          <span class="live__dot" aria-hidden="true" />
          {{ liveLabel }}
        </span>
        <time v-if="lastUpdated" class="muted updated" data-testid="last-updated" :datetime="lastUpdated.toISOString()">
          Updated {{ lastUpdated.toLocaleTimeString() }}
        </time>
        <button class="button" type="button" data-testid="refresh-deployments" :disabled="loading" @click="load(false)">
          Refresh
        </button>
      </template>
    </PageHeader>

    <p v-if="error" class="notice project-error" data-tone="danger" data-testid="project-error">
      {{ error }}
    </p>

    <div class="columns">
      <div class="column">
        <section class="panel">
          <div class="panel__header">
            <h2>Environments</h2>
            <button
              v-if="!envFormOpen"
              class="button button--small"
              type="button"
              data-testid="new-environment"
              @click="envFormOpen = true"
            >
              New environment
            </button>
          </div>

          <form
            v-if="envFormOpen"
            class="env-form"
            data-testid="create-environment-form"
            novalidate
            @submit.prevent="submitEnvironment"
          >
            <div class="env-form__grid">
              <div class="field">
                <label for="environment-name">Name</label>
                <input
                  id="environment-name"
                  v-model="envName"
                  class="input"
                  autocomplete="off"
                  data-testid="environment-name-input"
                >
              </div>
              <div class="field">
                <label for="environment-kind">Kind</label>
                <select id="environment-kind" v-model="envKind" class="select" data-testid="environment-kind-input">
                  <option value="preview">Preview</option>
                  <option value="production">Production</option>
                </select>
              </div>
            </div>
            <div class="env-form__actions">
              <button class="button button--primary button--small" type="submit" data-testid="create-environment" :disabled="creatingEnv">
                Create environment
              </button>
              <button class="button button--small" type="button" data-testid="cancel-environment" @click="envFormOpen = false">
                Cancel
              </button>
            </div>
            <p class="form-error" role="alert" data-testid="environment-error">{{ envError ?? "" }}</p>
          </form>

          <LoadingBlock v-if="loading" :rows="2" label="Loading environments" />

          <EmptyState
            v-else-if="environments.length === 0"
            title="No environments"
            message="Deployments land in environments. Create a preview or production one to start."
            testid="environments-empty"
          >
            <template #action>
              <button class="button" type="button" data-testid="environments-empty-create" @click="envFormOpen = true">
                Create an environment
              </button>
            </template>
          </EmptyState>

          <ul v-else class="environments" data-testid="environments">
            <li
              v-for="environment in environments"
              :key="environment.id"
              class="environment"
              data-testid="environment"
              :data-environment-id="environment.id"
              :data-environment-kind="environment.kind"
            >
              <div class="environment__head">
                <span class="environment__name" data-testid="environment-name">{{ environment.name }}</span>
                <StatusBadge :status="environment.kind" testid="environment-kind" />
                <StatusBadge :status="environment.status" testid="environment-status" />
              </div>
              <p class="environment__version">
                <span class="muted">Current version</span>
                <span class="mono" data-testid="environment-version">{{ environment.currentVersion ?? "none" }}</span>
              </p>
              <button
                v-if="environment.kind === 'preview' && environment.currentVersion && production"
                class="button button--small"
                type="button"
                data-testid="promote"
                :disabled="promoting === environment.id"
                @click="promote(environment)"
              >
                Promote to {{ production.name }}
              </button>
            </li>
          </ul>
        </section>

        <section class="panel">
          <div class="panel__header">
            <h2>Start a deployment</h2>
          </div>
          <form class="panel__body deploy" data-testid="deploy-form" novalidate @submit.prevent="submitDeployment">
            <div class="field">
              <label for="deploy-environment">Environment</label>
              <select
                id="deploy-environment"
                v-model="environmentId"
                class="select"
                data-testid="deploy-environment"
                :disabled="environments.length === 0"
              >
                <option v-if="environments.length === 0" value="">No environments yet</option>
                <option v-for="environment in environments" :key="environment.id" :value="environment.id">
                  {{ environment.name }} ({{ environment.kind }})
                </option>
              </select>
            </div>
            <div class="field">
              <label for="deploy-version">Version</label>
              <input
                id="deploy-version"
                v-model="version"
                class="input"
                autocomplete="off"
                placeholder="1.4.0"
                data-testid="deploy-version"
              >
            </div>
            <div class="field">
              <label for="deploy-commit">Commit SHA</label>
              <input
                id="deploy-commit"
                v-model="commit"
                class="input mono"
                autocomplete="off"
                spellcheck="false"
                placeholder="9f3c1a2"
                data-testid="deploy-commit"
              >
            </div>
            <button
              class="button button--primary"
              type="submit"
              data-testid="deploy-submit"
              :disabled="deploying || environments.length === 0"
            >
              Deploy
            </button>
            <p class="form-error" role="alert" data-testid="deploy-error">{{ deployError ?? "" }}</p>
          </form>
        </section>
      </div>

      <section class="panel history">
        <div class="panel__header">
          <h2>Deployments</h2>
          <span class="muted history__count">{{ deployments.length }} shown</span>
        </div>

        <LoadingBlock v-if="loading" :rows="4" label="Loading deployments" />

        <EmptyState
          v-else-if="deployments.length === 0"
          title="No deployments yet"
          message="Deploy a version to an environment and it will appear here."
          testid="deployments-empty"
        />

        <ol v-else class="rail" data-testid="deployments">
          <li
            v-for="deployment in deployments"
            :key="deployment.id"
            class="release"
            data-testid="deployment-row"
            :data-deployment-id="deployment.id"
            :data-deployment-status="deployment.status"
          >
            <span class="release__node" :data-tone="deployment.status" aria-hidden="true" />
            <div class="release__body">
              <div class="release__top">
                <span class="mono release__version" data-testid="deployment-version">{{ deployment.version }}</span>
                <StatusBadge :status="deployment.status" testid="deployment-status" />
                <span class="muted release__environment" data-testid="deployment-environment">
                  {{ environmentNames.get(deployment.environmentId) ?? deployment.environmentId }}
                </span>
              </div>
              <p class="release__meta muted">
                <span class="mono" data-testid="deployment-sha">{{ shortSha(deployment.commitSha) }}</span>
                · {{ deployment.requestedBy }}
                · {{ deployment.deployMinutes }} min
                · <time :datetime="deployment.completedAtUtc ?? deployment.createdAtUtc" data-testid="deployment-time">
                  {{ formatDateTime(deployment.completedAtUtc ?? deployment.createdAtUtc) }}
                </time>
              </p>
            </div>
            <button
              v-if="canRollback(deployment)"
              class="button button--small"
              type="button"
              data-testid="rollback"
              :disabled="rollingBack === deployment.id"
              @click="performRollback(deployment)"
            >
              Roll back
            </button>
          </li>
        </ol>
      </section>
    </div>
  </section>
</template>

<style scoped>
.project-error {
  margin-bottom: 16px;
}

.columns {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 340px), 1fr));
  gap: 16px;
  align-items: start;
}

.column {
  display: grid;
  gap: 16px;
}

.live {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  border: 1px solid var(--ns-line);
  border-radius: 999px;
  padding: 3px 10px;
  color: var(--ns-muted);
  font-size: 12.5px;
  font-weight: 600;
}

.live__dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: var(--ns-faint);
}

.live[data-state="live"] {
  border-color: var(--ns-success);
  color: var(--ns-success);
}

.live[data-state="live"] .live__dot {
  background: var(--ns-success);
}

.live[data-state="polling"] {
  border-color: var(--ns-warning);
  color: var(--ns-warning);
}

.live[data-state="polling"] .live__dot {
  background: var(--ns-warning);
}

.updated {
  font-size: 12.5px;
}

.env-form {
  display: grid;
  gap: 10px;
  padding: 14px 18px;
  border-bottom: 1px solid var(--ns-line-soft);
  background: #f8fbfa;
}

.env-form__grid {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
  gap: 10px;
}

.env-form__actions {
  display: flex;
  gap: 8px;
}

.environments {
  margin: 0;
  padding: 0;
  list-style: none;
}

.environment {
  display: grid;
  gap: 8px;
  justify-items: start;
  padding: 14px 18px;
  border-bottom: 1px solid var(--ns-line-soft);
}

.environment:last-child {
  border-bottom: 0;
}

.environment__head {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.environment__name {
  font-weight: 600;
}

.environment__version {
  display: flex;
  gap: 8px;
  font-size: 13.5px;
}

.deploy {
  display: grid;
  gap: 12px;
}

.form-error {
  min-height: 20px;
  margin: 0;
  color: var(--ns-danger);
  font-size: 13.5px;
}

.history__count {
  font-size: 13px;
}

.rail {
  margin: 0;
  padding: 0;
  list-style: none;
}

.release {
  position: relative;
  display: flex;
  align-items: flex-start;
  gap: 12px;
  padding: 14px 18px 14px 30px;
  border-bottom: 1px solid var(--ns-line-soft);
}

.release:last-child {
  border-bottom: 0;
}

.release__node {
  position: absolute;
  left: 14px;
  top: 19px;
  width: 9px;
  height: 9px;
  border-radius: 50%;
  background: var(--ns-neutral);
}

.release__node[data-tone="succeeded"] {
  background: var(--ns-success);
}

.release__node[data-tone="failed"] {
  background: var(--ns-danger);
}

.release__node[data-tone="rolled_back"] {
  background: var(--ns-warning);
}

.release__body {
  flex: 1;
  min-width: 0;
}

.release__top {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.release__version {
  font-size: 14px;
  font-weight: 500;
}

.release__meta {
  margin-top: 3px;
  font-size: 12.5px;
}
</style>
