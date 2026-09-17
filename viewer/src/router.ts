import { ref } from "vue";

export type Route =
  | { name: "run" }
  | { name: "styleguide" }
  | { name: "test"; testId: string; tab: string; entryId?: string };

export const defaultTestTab = "story";

export const route = ref<Route>(parse(location.hash));

function parse(hash: string): Route {
  if (hash.startsWith("#/styleguide")) return { name: "styleguide" };
  const match = /^#\/test\/([^/?#]+)(?:\/([a-z]+))?(?:\?(.*))?/.exec(hash);
  if (match) {
    // A selected entry belongs in the address: a link to a failure has to survive a reload.
    const entryId = new URLSearchParams(match[3] ?? "").get("entry") ?? undefined;
    return { name: "test", testId: decodeURIComponent(match[1]), tab: match[2] ?? defaultTestTab, entryId };
  }
  return { name: "run" };
}

export function href(next: Route): string {
  if (next.name === "styleguide") return "#/styleguide";
  if (next.name === "run") return "#/";
  const base = `#/test/${encodeURIComponent(next.testId)}/${next.tab}`;
  return next.entryId ? `${base}?entry=${encodeURIComponent(next.entryId)}` : base;
}

export function navigate(next: Route) {
  const target = href(next);
  if (location.hash === target) route.value = next;
  else location.hash = target;
}

/** Changes the selection without adding a history entry for every click in a tree. */
export function replace(next: Route) {
  const target = href(next);
  if (location.hash === target) { route.value = next; return; }
  history.replaceState(null, "", target);
  route.value = next;
}

addEventListener("hashchange", () => { route.value = parse(location.hash); });
