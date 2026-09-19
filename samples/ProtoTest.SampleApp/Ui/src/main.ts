import { createApp } from "vue";
import App from "./App.vue";
import { setUnauthorizedHandler } from "./api";
import { router } from "./router";
import { session } from "./session";
import "./styles/base.css";

setUnauthorizedHandler(() => {
  session.markAnonymous();
  void router.push({ name: "signin" });
});

createApp(App).use(router).mount("#app");
