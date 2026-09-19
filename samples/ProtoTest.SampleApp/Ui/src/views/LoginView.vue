<script setup lang="ts">
import { ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import { ApiProblem } from "../api";
import { session } from "../session";

const route = useRoute();
const router = useRouter();
const token = ref("");
const error = ref<string | null>(null);
const busy = ref(false);

async function submit(): Promise<void> {
  const value = token.value.trim();
  if (!value) {
    error.value = "Enter an API token.";
    return;
  }

  busy.value = true;
  error.value = null;
  try {
    await session.signIn(value);
    const redirect = typeof route.query.redirect === "string" ? route.query.redirect : "/console/";
    await router.push(redirect);
  } catch (cause) {
    error.value =
      cause instanceof ApiProblem
        ? cause.message
        : "Sign-in failed. Check that the Northstar application is running.";
  } finally {
    busy.value = false;
  }
}
</script>

<template>
  <section class="login" data-testid="login-page">
    <div class="login__card panel">
      <div class="login__intro">
        <h1>Sign in to Northstar</h1>
        <p class="muted">
          Northstar is the release control plane for your organization. Paste a tenant API token to open
          the console.
        </p>
      </div>

      <form class="login__form" data-testid="login-form" novalidate @submit.prevent="submit">
        <div class="field">
          <label for="login-token">API token</label>
          <input
            id="login-token"
            v-model="token"
            class="input"
            type="password"
            name="token"
            autocomplete="off"
            spellcheck="false"
            data-testid="login-token"
            :aria-invalid="error ? 'true' : undefined"
          >
        </div>

        <button class="button button--primary" type="submit" data-testid="login-submit" :disabled="busy">
          Sign in
        </button>

        <p class="login__error" role="alert" data-testid="login-error">{{ error ?? "" }}</p>
      </form>

      <p v-if="session.state.status === 'error'" class="notice" data-tone="danger" data-testid="login-unreachable">
        {{ session.state.error }}
      </p>
    </div>
  </section>
</template>

<style scoped>
.login {
  display: grid;
  justify-items: center;
  padding-top: clamp(24px, 8vh, 96px);
}

.login__card {
  width: min(440px, 100%);
  padding: 30px 30px 24px;
}

.login__intro {
  display: grid;
  gap: 8px;
  margin-bottom: 22px;
}

.login__form {
  display: grid;
  gap: 14px;
}

.login__error {
  min-height: 21px;
  margin: 0;
  color: var(--ns-danger);
  font-size: 13.5px;
}
</style>
