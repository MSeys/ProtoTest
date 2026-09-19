/**
 * The console's only door to the application. Every screen imports typed calls from here so the test
 * hooks in README.md and the network calls stay in one place.
 *
 * Auth, in the order the client prefers it:
 *  - an API-created session: a bearer token kept in sessionStorage (see `setBearerToken`), and
 *  - the browser session: the same HttpOnly cookie the /login form sets, sent with every request.
 */

const TOKEN_KEY = "northstar.console.token";

export interface Organization {
  id: string;
  slug: string;
  name: string;
  planId: string;
  planName: string;
  status: string;
  seatCount: number;
  seatLimit: number | null;
  projectCount: number;
  projectLimit: number | null;
  cancelAtPeriodEnd: boolean;
  createdAtUtc: string;
  currentPeriodStartUtc: string;
  currentPeriodEndUtc: string;
}

export interface Subscription {
  planId: string;
  planName: string;
  status: string;
  seats: number;
  includedSeats: number;
  monthlyBasePrice: number;
  extraSeatPrice: number;
  includedDeployMinutes: number;
  cancelAtPeriodEnd: boolean;
  currentPeriodStartUtc: string;
  currentPeriodEndUtc: string;
}

export interface Project {
  id: string;
  name: string;
  slug: string;
  status: string;
  environmentCount: number;
  createdAtUtc: string;
}

export interface Environment {
  id: string;
  projectId: string;
  name: string;
  kind: string;
  status: string;
  currentVersion: string | null;
  createdAtUtc: string;
}

export interface Deployment {
  id: string;
  projectId: string;
  environmentId: string;
  version: string;
  commitSha: string;
  status: string;
  requestedBy: string;
  deployMinutes: number;
  createdAtUtc: string;
  completedAtUtc: string | null;
}

export interface Payment {
  id: number;
  amount: number;
  status: string;
  method: string;
  failureReason: string | null;
  attemptedAtUtc: string;
}

export interface InvoiceLine {
  description: string;
  quantity: number;
  unitPrice: number;
  amount: number;
}

export interface Invoice {
  id: number;
  number: string;
  status: string;
  periodStartUtc: string;
  periodEndUtc: string;
  issuedAtUtc: string;
  dueAtUtc: string;
  paidAtUtc: string | null;
  subtotal: number;
  tax: number;
  total: number;
  lines: InvoiceLine[];
  payments: Payment[];
}

export interface UsageSummary {
  metric: string;
  total: number;
  included: number;
  overage: number;
  fromUtc: string;
  toUtc: string;
}

export interface CursorPage<T> {
  items: T[];
  nextCursor: string | null;
  hasMore: boolean;
  totalCount: number;
}

/** The application's `application/problem+json` envelope, as an Error. */
export class ApiProblem extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    message: string,
    readonly details: Record<string, string> | null = null
  ) {
    super(message);
    this.name = "ApiProblem";
  }

  get isUnauthorized(): boolean {
    return this.status === 401;
  }
}

let onUnauthorized: (() => void) | null = null;

/** Registered once by main.ts so any screen that loses its session is sent back to sign-in. */
export function setUnauthorizedHandler(handler: () => void): void {
  onUnauthorized = handler;
}

export function bearerToken(): string | null {
  return sessionStorage.getItem(TOKEN_KEY);
}

export function setBearerToken(token: string | null): void {
  if (token) {
    sessionStorage.setItem(TOKEN_KEY, token);
  } else {
    sessionStorage.removeItem(TOKEN_KEY);
  }
}

export function hasBearerToken(): boolean {
  return bearerToken() !== null;
}

interface RequestOptions {
  body?: unknown;
  query?: Record<string, string | number | boolean | undefined | null>;
  suppressUnauthorizedRedirect?: boolean;
}

function url(path: string, query?: RequestOptions["query"]): string {
  const base = new URL(path, window.location.origin);
  for (const [key, value] of Object.entries(query ?? {})) {
    if (value !== undefined && value !== null) {
      base.searchParams.set(key, String(value));
    }
  }
  return base.pathname + base.search;
}

async function request<T>(method: string, path: string, options: RequestOptions = {}): Promise<T> {
  const headers: Record<string, string> = { Accept: "application/json" };
  const token = bearerToken();
  if (token) {
    headers.Authorization = `Bearer ${token}`;
  }
  if (options.body !== undefined) {
    headers["Content-Type"] = "application/json";
  }

  let response: Response;
  try {
    response = await fetch(url(path, options.query), {
      method,
      headers,
      credentials: "same-origin",
      body: options.body === undefined ? undefined : JSON.stringify(options.body)
    });
  } catch {
    throw new ApiProblem(0, "network_error", "The Northstar API is unreachable.");
  }

  if (response.status === 401 && !options.suppressUnauthorizedRedirect) {
    onUnauthorized?.();
  }

  if (!response.ok) {
    throw await problemFrom(response);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

async function problemFrom(response: Response): Promise<ApiProblem> {
  try {
    const payload = (await response.json()) as {
      code?: string;
      message?: string;
      detail?: string;
      title?: string;
      details?: Record<string, string>;
    };
    return new ApiProblem(
      response.status,
      payload.code ?? "http_error",
      payload.message ?? payload.detail ?? payload.title ?? `Request failed with ${response.status}.`,
      payload.details ?? null
    );
  } catch {
    return new ApiProblem(response.status, "http_error", `Request failed with ${response.status}.`);
  }
}

// ---------------------------------------------------------------- session

/** Exchanges a token for the same HttpOnly cookie the server-rendered login form sets. */
export async function signIn(token: string): Promise<void> {
  await request<{ authenticated: boolean }>("POST", "/api/auth/login", {
    body: { token },
    suppressUnauthorizedRedirect: true
  });
  setBearerToken(null);
}

/** Clears the cookie session and any bearer token the console is holding. */
export async function signOut(): Promise<void> {
  try {
    await request<void>("POST", "/api/auth/logout", { suppressUnauthorizedRedirect: true });
  } catch {
    // Signing out is best-effort: the local state is cleared either way.
  }
  setBearerToken(null);
}

// ---------------------------------------------------------------- REST

export function getOrganization(): Promise<Organization> {
  return request<Organization>("GET", "/api/v1/organization");
}

export function getSubscription(): Promise<Subscription> {
  return request<Subscription>("GET", "/api/v1/subscription");
}

export function listProjects(limit = 100): Promise<CursorPage<Project>> {
  return request<CursorPage<Project>>("GET", "/api/v1/projects", { query: { limit } });
}

export function createProject(name: string): Promise<Project> {
  return request<Project>("POST", "/api/v1/projects", { body: { name } });
}

export function getProject(projectId: string): Promise<Project> {
  return request<Project>("GET", `/api/v1/projects/${encodeURIComponent(projectId)}`);
}

export function listEnvironments(projectId: string): Promise<CursorPage<Environment>> {
  return request<CursorPage<Environment>>("GET", `/api/v1/projects/${encodeURIComponent(projectId)}/environments`, {
    query: { limit: 100 }
  });
}

export function createEnvironment(projectId: string, name: string, kind: string): Promise<Environment> {
  return request<Environment>("POST", `/api/v1/projects/${encodeURIComponent(projectId)}/environments`, {
    body: { name, kind }
  });
}

export function listDeployments(projectId: string, limit = 50): Promise<CursorPage<Deployment>> {
  return request<CursorPage<Deployment>>("GET", "/api/v1/deployments", {
    query: { projectId, limit }
  });
}

export function deploy(environmentId: string, version: string, commitSha: string): Promise<Deployment> {
  return request<Deployment>("POST", `/api/v1/environments/${encodeURIComponent(environmentId)}/deployments`, {
    body: { version, commitSha }
  });
}

export function rollback(deploymentId: string): Promise<Deployment> {
  return request<Deployment>("POST", `/api/v1/deployments/${encodeURIComponent(deploymentId)}/rollback`);
}

export function getUsageSummary(metric = "deploy_minutes"): Promise<UsageSummary> {
  return request<UsageSummary>("GET", "/api/v1/usage/summary", { query: { metric } });
}

export function listInvoices(limit = 100): Promise<CursorPage<Invoice>> {
  return request<CursorPage<Invoice>>("GET", "/api/v1/invoices", { query: { limit } });
}

/** Paying publishes the `invoice.paid` event and returns the invoice with its new payment. */
export function payInvoice(invoiceId: number, method: string): Promise<Invoice> {
  return request<Invoice>("POST", `/api/v1/invoices/${invoiceId}/pay`, { body: { method } });
}

// ---------------------------------------------------------------- GraphQL

interface GraphQLResponse<T> {
  data?: T;
  errors?: { message: string; extensions?: { code?: string } }[];
}

export async function graphql<T>(query: string, variables?: Record<string, unknown>): Promise<T> {
  const response = await fetch("/graphql", {
    method: "POST",
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json",
      ...(bearerToken() ? { Authorization: `Bearer ${bearerToken()}` } : {})
    },
    credentials: "same-origin",
    body: JSON.stringify({ query, variables })
  });

  if (response.status === 401) {
    onUnauthorized?.();
  }
  if (!response.ok) {
    throw await problemFrom(response);
  }

  const payload = (await response.json()) as GraphQLResponse<T>;
  if (payload.errors?.length) {
    const first = payload.errors[0];
    throw new ApiProblem(response.status, first.extensions?.code ?? "graphql_error", first.message);
  }
  return payload.data as T;
}

// ---------------------------------------------------------------- artifacts

async function artifact(path: string): Promise<Blob> {
  const token = bearerToken();
  const response = await fetch(path, {
    headers: {
      Accept: "*/*",
      ...(token ? { Authorization: `Bearer ${token}` } : {})
    },
    credentials: "same-origin"
  });
  if (!response.ok) {
    throw await problemFrom(response);
  }
  return response.blob();
}

/** Fetches an authenticated artifact and hands it to the browser as a download. */
export async function downloadArtifact(path: string, filename: string): Promise<void> {
  const blob = await artifact(path);
  const href = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = href;
  anchor.download = filename;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  URL.revokeObjectURL(href);
}

/** Opens an authenticated HTML artifact in a new tab (a bearer session cannot use a plain link). */
export async function openArtifact(path: string): Promise<void> {
  const blob = await artifact(path);
  const href = URL.createObjectURL(blob);
  window.open(href, "_blank", "noopener");
  window.setTimeout(() => URL.revokeObjectURL(href), 60_000);
}
