/** Where the open trace came from: a bundled demo, a hosted URL, or a local file. */
export type TraceSource =
  | { kind: "demo"; key: string }
  | { kind: "remote"; url: string }
  | { kind: "file" };

export type TraceParam =
  | { state: "absent" }
  | { state: "valid"; url: string }
  | { state: "invalid"; value: string };

/*
 * ?trace=<absolute-url> opens a hosted trace. Only absolute http(s) URLs qualify: anything else is
 * reported, not fetched, so a typo never looks like a CORS failure.
 */
export function parseTraceParam(value: string | null): TraceParam {
  if (value === null || value === "") return { state: "absent" };
  try {
    const parsed = new URL(value);
    if (parsed.protocol !== "http:" && parsed.protocol !== "https:") return { state: "invalid", value };
    return { state: "valid", url: value };
  } catch {
    return { state: "invalid", value };
  }
}

/** The name a fetched trace opens under: the URL's last segment, or its host when there is none. */
export function traceNameFromUrl(url: string): string {
  try {
    const parsed = new URL(url);
    const last = parsed.pathname.split("/").filter(Boolean).pop();
    return last ? decodeURIComponent(last) : parsed.host;
  } catch {
    return url;
  }
}

/*
 * The link the copy control hands out: the demo or trace URL behind the open trace, keeping the
 * reader's hash so a link to a failing check shares as one. A local file has no URL to share.
 */
export function shareUrl(origin: string, path: string, source: TraceSource, hash: string): string | null {
  if (source.kind === "file") return null;
  const query = source.kind === "demo"
    ? `demo=${source.key === "full" ? "1" : encodeURIComponent(source.key)}`
    : `trace=${encodeURIComponent(source.url)}`;
  return `${origin}${path}?${query}${hash}`;
}

/** Copies text to the clipboard, falling back to a selected field where the async API is missing. */
export async function copyText(text: string): Promise<boolean> {
  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(text);
      return true;
    }
  } catch {
    /* A denied permission falls through to the field fallback below. */
  }
  try {
    const field = document.createElement("textarea");
    field.value = text;
    field.setAttribute("readonly", "");
    field.style.position = "fixed";
    field.style.opacity = "0";
    document.body.append(field);
    field.select();
    const copied = document.execCommand("copy");
    field.remove();
    return copied;
  } catch {
    return false;
  }
}
