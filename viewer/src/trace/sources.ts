import { shallowRef } from "vue";

type SourceReader = (path: string) => Promise<string | undefined>;

// The open trace's embedded sources. Set when a trace opens; any view can ask for a file by its recorded path.
const reader = shallowRef<SourceReader | undefined>();
const cache = new Map<string, Promise<string | undefined>>();

export function useSources(next: SourceReader | undefined) {
  reader.value = next;
  cache.clear();
}

export function readSource(path: string): Promise<string | undefined> {
  const read = reader.value;
  if (!read) return Promise.resolve(undefined);
  let pending = cache.get(path);
  if (!pending) {
    pending = read(path).catch(() => undefined);
    cache.set(path, pending);
  }
  return pending;
}

export interface SourceLocation {
  file: string;
  line: number;
  functionName?: string;
}

/** The location an operation recorded with the OpenTelemetry code.* attributes, if it recorded one. */
export function sourceLocation(attributes: Record<string, unknown>): SourceLocation | undefined {
  const file = attributes["code.file.path"];
  const line = Number(attributes["code.line.number"]);
  if (typeof file !== "string" || !file || !Number.isFinite(line) || line < 1) return undefined;
  const functionName = attributes["code.function.name"];
  return { file, line, functionName: typeof functionName === "string" ? functionName : undefined };
}

export function fileName(path: string): string {
  return path.split(/[\/]/).pop() ?? path;
}
