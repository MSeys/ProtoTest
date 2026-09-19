import { createRouter, createWebHistory, type RouteRecordRaw } from "vue-router";
import { session } from "./session";
import DashboardView from "./views/DashboardView.vue";
import LoginView from "./views/LoginView.vue";
import NotFoundView from "./views/NotFoundView.vue";
import ProjectDetailView from "./views/ProjectDetailView.vue";
import ProjectsView from "./views/ProjectsView.vue";
import BillingView from "./views/BillingView.vue";
import ReportsView from "./views/ReportsView.vue";

export const routes: RouteRecordRaw[] = [
  {
    path: "/console/signin",
    name: "signin",
    component: LoginView,
    meta: { public: true, title: "Sign in" }
  },
  {
    path: "/console/",
    name: "dashboard",
    component: DashboardView,
    meta: { title: "Dashboard" }
  },
  {
    path: "/console/projects",
    name: "projects",
    component: ProjectsView,
    meta: { title: "Projects" }
  },
  {
    path: "/console/projects/:projectId",
    name: "project",
    component: ProjectDetailView,
    props: true,
    meta: { title: "Project" }
  },
  {
    path: "/console/billing",
    name: "billing",
    component: BillingView,
    meta: { title: "Billing" }
  },
  {
    path: "/console/reports",
    name: "reports",
    component: ReportsView,
    meta: { title: "Reports" }
  },
  {
    path: "/console/:pathMatch(.*)*",
    name: "not-found",
    component: NotFoundView,
    meta: { public: true, title: "Not found" }
  }
];

export const router = createRouter({
  history: createWebHistory(),
  routes,
  scrollBehavior: () => ({ top: 0 })
});

router.beforeEach(async (to) => {
  await session.ensureInitialized();

  if (to.meta.public === true || session.state.organization) {
    return true;
  }

  return {
    name: "signin",
    query: to.fullPath === "/console/" ? {} : { redirect: to.fullPath }
  };
});

router.afterEach((to) => {
  const title = (to.meta.title as string | undefined) ?? "Console";
  document.title = `${title} · Northstar`;
});
