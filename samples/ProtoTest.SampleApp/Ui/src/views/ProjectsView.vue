<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from "vue";
import { ApiProblem, createProject, listProjects, type Project } from "../api";
import EmptyState from "../components/EmptyState.vue";
import LoadingBlock from "../components/LoadingBlock.vue";
import PageHeader from "../components/PageHeader.vue";
import StatusBadge from "../components/StatusBadge.vue";
import { formatDate } from "../format";
import { toast } from "../toasts";

const projects = ref<Project[]>([]);
const loading = ref(true);
const loadError = ref<string | null>(null);
const search = ref("");
const formOpen = ref(false);
const name = ref("");
const formError = ref<string | null>(null);
const submitting = ref(false);
const nameInput = ref<HTMLInputElement | null>(null);

const filtered = computed(() => {
  const query = search.value.trim().toLowerCase();
  return query ? projects.value.filter((project) => project.name.toLowerCase().includes(query)) : projects.value;
});

async function load(): Promise<void> {
  loading.value = true;
  loadError.value = null;
  try {
    projects.value = (await listProjects()).items;
  } catch (cause) {
    loadError.value = cause instanceof ApiProblem ? cause.message : "The project list could not be loaded.";
  } finally {
    loading.value = false;
  }
}

async function openForm(): Promise<void> {
  formOpen.value = true;
  await nextTick();
  nameInput.value?.focus();
}

function closeForm(): void {
  formOpen.value = false;
  name.value = "";
  formError.value = null;
}

async function submit(): Promise<void> {
  const value = name.value.trim();
  if (!value) {
    formError.value = "Enter a project name.";
    return;
  }
  if (value.length > 60) {
    formError.value = "Project names are 60 characters or fewer.";
    return;
  }

  submitting.value = true;
  formError.value = null;
  try {
    const project = await createProject(value);
    projects.value = [...projects.value, project];
    closeForm();
    toast("success", `${project.name} created.`);
  } catch (cause) {
    if (cause instanceof ApiProblem && cause.code === "plan_limit_exceeded") {
      formError.value = `${cause.message} Change the plan on the Billing screen to add more.`;
    } else {
      formError.value =
        cause instanceof ApiProblem ? cause.message : "The project could not be created.";
    }
  } finally {
    submitting.value = false;
  }
}

onMounted(load);
</script>

<template>
  <section data-testid="projects-page">
    <PageHeader
      title="Projects"
      description="Everything your organization deploys, and the environments each project owns."
      testid="projects-title"
    >
      <template #actions>
        <button
          v-if="!formOpen"
          class="button button--primary"
          type="button"
          data-testid="new-project"
          @click="openForm"
        >
          New project
        </button>
      </template>
    </PageHeader>

    <form
      v-if="formOpen"
      class="create panel"
      data-testid="create-project-form"
      novalidate
      @submit.prevent="submit"
    >
      <div class="create__row">
        <div class="field create__field">
          <label for="project-name">Project name</label>
          <input
            id="project-name"
            ref="nameInput"
            v-model="name"
            class="input"
            name="name"
            maxlength="60"
            autocomplete="off"
            data-testid="project-name-input"
            :aria-invalid="formError ? 'true' : undefined"
          >
        </div>
        <button class="button button--primary" type="submit" data-testid="create-project" :disabled="submitting">
          Create project
        </button>
        <button class="button" type="button" data-testid="cancel-project" @click="closeForm">Cancel</button>
      </div>
      <p class="create__error" role="alert" data-testid="create-project-error">{{ formError ?? "" }}</p>
    </form>

    <div class="panel">
      <div class="panel__header">
        <h2>All projects</h2>
        <div class="field search">
          <label class="visually-hidden" for="project-search">Search projects</label>
          <input
            id="project-search"
            v-model="search"
            class="input"
            type="search"
            placeholder="Filter by name"
            data-testid="project-search"
          >
        </div>
      </div>

      <LoadingBlock v-if="loading" :rows="4" label="Loading projects" />

      <div v-else-if="loadError" class="panel__body">
        <p class="notice" data-tone="danger" data-testid="projects-error">{{ loadError }}</p>
        <button class="button" type="button" style="margin-top: 12px" data-testid="projects-retry" @click="load">
          Retry
        </button>
      </div>

      <EmptyState
        v-else-if="projects.length === 0"
        title="No projects yet"
        message="A project holds the environments you deploy to. Create the first one."
        testid="projects-empty"
      >
        <template #action>
          <button class="button button--primary" type="button" data-testid="projects-empty-create" @click="openForm">
            Create a project
          </button>
        </template>
      </EmptyState>

      <EmptyState
        v-else-if="filtered.length === 0"
        title="No matching projects"
        :message="`Nothing matches “${search.trim()}”.`"
        testid="projects-no-match"
      >
        <template #action>
          <button class="button" type="button" data-testid="projects-clear-search" @click="search = ''">
            Clear search
          </button>
        </template>
      </EmptyState>

      <div v-else class="table-scroll">
        <table class="table" data-testid="projects-table">
          <thead>
            <tr>
              <th scope="col">Project</th>
              <th scope="col">Status</th>
              <th scope="col">Environments</th>
              <th scope="col">Created</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="project in filtered" :key="project.id" data-testid="project-row" :data-project-id="project.id">
              <td>
                <RouterLink
                  class="project-link"
                  :to="`/console/projects/${project.id}`"
                  data-testid="project-link"
                >
                  {{ project.name }}
                </RouterLink>
              </td>
              <td><StatusBadge :status="project.status" testid="project-status" /></td>
              <td data-testid="project-environments">{{ project.environmentCount }}</td>
              <td class="muted" data-testid="project-created">{{ formatDate(project.createdAtUtc) }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  </section>
</template>

<style scoped>
.create {
  margin-bottom: 18px;
  padding: 16px 18px 10px;
}

.create__row {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: 10px;
}

.create__field {
  flex: 1 1 260px;
  max-width: 420px;
}

.create__error {
  min-height: 20px;
  margin: 8px 0 0;
  color: var(--ns-danger);
  font-size: 13.5px;
}

.search {
  width: min(240px, 100%);
}
</style>
