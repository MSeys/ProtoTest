import { reactive, readonly } from "vue";
import * as api from "./api";

export type SessionStatus = "unknown" | "anonymous" | "ready" | "error";

interface SessionState {
  status: SessionStatus;
  organization: api.Organization | null;
  error: string | null;
}

const state = reactive<SessionState>({
  status: "unknown",
  organization: null,
  error: null
});

let started: Promise<void> | null = null;

/**
 * A bearer session can be handed to the console as `?token=...`, which is how a journey uses a token the
 * test-support API created. The query parameter is consumed once and never kept in the address bar.
 */
function consumeTokenFromUrl(): void {
  const url = new URL(window.location.href);
  const token = url.searchParams.get("token");
  if (!token) {
    return;
  }

  api.setBearerToken(token);
  url.searchParams.delete("token");
  window.history.replaceState({}, "", url.pathname + url.search + url.hash);
}

async function load(): Promise<void> {
  consumeTokenFromUrl();
  try {
    state.organization = await api.getOrganization();
    state.status = "ready";
    state.error = null;
  } catch (error) {
    state.organization = null;
    if (error instanceof api.ApiProblem && error.status === 401) {
      api.setBearerToken(null);
      state.status = "anonymous";
      state.error = null;
    } else {
      state.status = "error";
      state.error = error instanceof Error ? error.message : "The Northstar API is unreachable.";
    }
  }
}

export const session = {
  state: readonly(state),

  /** Runs the first load exactly once; the router guard awaits this before every navigation. */
  ensureInitialized(): Promise<void> {
    started ??= load();
    return started;
  },

  async signIn(token: string): Promise<void> {
    await api.signIn(token);
    state.organization = await api.getOrganization();
    state.status = "ready";
    state.error = null;
  },

  async signOut(): Promise<void> {
    await api.signOut();
    state.organization = null;
    state.status = "anonymous";
    state.error = null;
  },

  markAnonymous(): void {
    if (state.status === "ready") {
      state.organization = null;
      state.status = "anonymous";
    }
  }
};
