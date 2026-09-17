import type { TraceEntry } from "./trace-schema";
import { entryTitle } from "./trace-format";

/** Text search across what a reader can see: titles, names, kinds and every recorded attribute. */
export function searchMatches(entry: TraceEntry, query: string): boolean {
  if (!query) return true;
  const needle = query.toLocaleLowerCase();
  if (entryTitle(entry).toLocaleLowerCase().includes(needle)) return true;
  if (entry.name.toLocaleLowerCase().includes(needle)) return true;
  if (entry.kind.toLocaleLowerCase().includes(needle)) return true;
  for (const value of Object.values(entry.attributes ?? {})) {
    if (value && value.toLocaleLowerCase().includes(needle)) return true;
  }
  return false;
}
