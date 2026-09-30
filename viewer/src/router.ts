import { ref } from "vue";

export type TestView = "steps" | "timeline" | "state" | "files";
export const testViews: TestView[] = ["steps", "timeline", "state", "files"];

/** Names a view had before, so a link shared then still opens the view it meant. */
const viewAliases: Record<string, TestView> = { story: "steps", spans: "timeline" };

/** What the inspector shows: an operation, or a tracked item by kind and id. */
export type Selection = { span: string } | { item: { kind: string; id: string } };

export type Route =
  | { name: "run" }
  | { name: "test"; testId: string; view: TestView; selection?: Selection };

export const route = ref<Route>(parse(location.hash));

export function parse(hash: string): Route {
  const match = /^#\/test\/([^/?#]+)(?:\/([a-z]+))?(?:\?(.*))?/.exec(hash);
  if (!match) return { name: "run" };
  const named = viewAliases[match[2] ?? ""] ?? match[2] ?? "";
  const view = (testViews as string[]).includes(named) ? named as TestView : "steps";
  // The selection belongs in the address: a link to a failure has to survive a reload and a share.
  const query = new URLSearchParams(match[3] ?? "");
  const span = query.get("span") ?? query.get("entry");
  const kind = query.get("kind");
  const id = query.get("item");
  const selection: Selection | undefined = span ? { span } : kind && id ? { item: { kind, id } } : undefined;
  return { name: "test", testId: decodeURIComponent(match[1]), view, selection };
}

export function href(next: Route): string {
  if (next.name === "run") return "#/";
  const base = `#/test/${encodeURIComponent(next.testId)}/${next.view}`;
  const selection = next.selection;
  if (!selection) return base;
  const query = "span" in selection
    ? new URLSearchParams({ span: selection.span })
    : new URLSearchParams({ kind: selection.item.kind, item: selection.item.id });
  return `${base}?${query}`;
}

export function navigate(next: Route) {
  const target = href(next);
  if (location.hash === target) route.value = next;
  else location.hash = target;
}

/** Changes the selection without a history entry for every click in a list. */
export function replace(next: Route) {
  const target = href(next);
  if (location.hash !== target) history.replaceState(null, "", target);
  route.value = next;
}

addEventListener("hashchange", () => { route.value = parse(location.hash); });
