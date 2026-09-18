import type { InjectionKey } from "vue";

/** The expand state a JSON tree shares with every node in it, so "expand all" reaches the deepest one. */
export interface JsonContext {
  isOpen(path: string, depth: number): boolean;
  toggle(path: string, depth: number): void;
  /** Paths to mark, such as the properties a shape check found wrong. */
  marks: Set<string>;
}

export const jsonContextKey: InjectionKey<JsonContext> = Symbol("json");

/** A value as JSON when it is JSON: an object or array itself, or a string that holds one. Undefined otherwise. */
export function parseEmbedded(value: string): unknown {
  const trimmed = value.trim();
  if (!(trimmed.startsWith("{") && trimmed.endsWith("}")) && !(trimmed.startsWith("[") && trimmed.endsWith("]"))) return undefined;
  try {
    const parsed: unknown = JSON.parse(trimmed);
    return parsed !== null && typeof parsed === "object" ? parsed : undefined;
  } catch {
    return undefined;
  }
}

/** Normalizes a shape path like `$.items[0].id` to the path the tree gives the same node. */
export function jsonPath(path: string): string {
  const trimmed = path.trim();
  return trimmed.startsWith("$") ? trimmed : `$.${trimmed}`;
}
