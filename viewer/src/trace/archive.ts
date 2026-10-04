import type { WireManifest, WireSpans, WireState } from "./wire";
import { openZip, type Zip } from "../artifacts/zip";

const decoder = new TextDecoder();
const MAX_ARCHIVE_BYTES = 250 * 1024 * 1024;
const MAX_ENTRIES = 50_000;

/**
 * Why a file could not be opened, so each case gets its own designed state: a trace from before the v2
 * wire, a file that is not a trace at all, or a trace that is too large or uses a feature we cannot read.
 */
export type TraceProblem = "legacy" | "corrupt" | "unsupported";

export class TraceOpenError extends Error {
  constructor(readonly problem: TraceProblem, message: string) {
    super(message);
  }
}

export interface TraceArchive {
  spans: WireSpans;
  state: WireState;
  /** Reads a file the trace declared as an artifact; anything else in the ZIP stays closed. */
  readFile(archivePath: string, mediaType: string): Promise<Blob>;
  /** Reads a source file the manifest embedded for a code.file.path, or undefined when it was not embedded. */
  readSource(path: string): Promise<string | undefined>;
}

export async function openTraceArchive(buffer: ArrayBuffer, unwrap = true): Promise<TraceArchive> {
  if (buffer.byteLength > MAX_ARCHIVE_BYTES) throw new TraceOpenError("unsupported", "This trace is larger than the 250 MiB the viewer reads.");
  const bytes = new Uint8Array(buffer);
  const zip = openZip(bytes, {
    maxEntries: MAX_ENTRIES,
    maxBytes: MAX_ARCHIVE_BYTES,
    createError: (problem, message) => new TraceOpenError(problem, message),
    vocabulary: {
      notArchive: "This file is not a ProtoTrace archive.",
      tooManyEntries: "This trace contains too many files.",
      directoryDamaged: "The trace's ZIP directory is damaged.",
      duplicate: name => `The trace contains duplicate entries named ${name}.`,
      missing: name => `The trace is missing ${name}.`,
      tooLarge: name => `${name} is too large to read.`,
      damaged: name => `${name} is damaged.`,
      invalidSize: name => `${name} has an invalid size.`,
      unsupportedCompression: "This trace uses a ZIP compression this browser cannot read.",
      decompressionFailed: name => `${name} could not be decompressed.`
    }
  });
  // A CI artifact download is a ZIP around the trace: open the one .prototrace inside it, so a trace from a
  // pull request opens without unpacking it first. The browser reads it locally; nothing is uploaded.
  if (unwrap && !zip.entries.has("manifest.json")) {
    const traces = [...zip.entries.keys()].filter(name => name.toLowerCase().endsWith(".prototrace"));
    if (traces.length === 1) {
      const inner = await zip.read(traces[0]);
      return openTraceArchive(inner.buffer.slice(inner.byteOffset, inner.byteOffset + inner.byteLength) as ArrayBuffer, false);
    }
    if (traces.length > 1) throw new TraceOpenError("unsupported", "This ZIP holds more than one trace; unpack it and open one.");
  }
  const manifest = await readJson<WireManifest>(zip, "manifest.json");
  if (!manifest.spansEntry || !manifest.stateEntry) {
    if (manifest.runEntry) throw new TraceOpenError("legacy", "This trace was written by an older ProtoTest version.");
    throw new TraceOpenError("corrupt", "The trace manifest names no documents.");
  }
  const spans = await readJson<WireSpans>(zip, manifest.spansEntry);
  const state = await readJson<WireState>(zip, manifest.stateEntry);
  if (!Array.isArray(spans.resourceSpans)) throw new TraceOpenError("corrupt", "spans.json has no resource groups.");
  if (!spans.formatVersion?.startsWith("2.")) throw new TraceOpenError("unsupported", `Span format ${spans.formatVersion} is not supported.`);
  // The state document carries its own version, and only the viewer knows what it can read: accept the
  // current 1.x wire and the 2.x bump that adds entity fields, reject anything else loudly.
  if (!state.formatVersion?.startsWith("1.") && !state.formatVersion?.startsWith("2."))
    throw new TraceOpenError("unsupported", `State format ${state.formatVersion} is not supported.`);
  // Only what the trace declares as an artifact can be opened; the rest of the ZIP stays closed.
  const declared = new Set(spans.resourceSpans.flatMap(group => (group.artifacts ?? []).map(artifact => artifact.archivePath)));
  return {
    spans,
    state,
    async readFile(archivePath, mediaType) {
      if (!declared.has(archivePath)) throw new Error("The requested file is not declared by this trace.");
      const content = await zip.read(archivePath);
      const copy = content.buffer.slice(content.byteOffset, content.byteOffset + content.byteLength) as ArrayBuffer;
      return new Blob([copy], { type: mediaType });
    },
    async readSource(path) {
      const archivePath = manifest.sources?.[path];
      if (!archivePath || !zip.entries.has(archivePath)) return undefined;
      return decoder.decode(await zip.read(archivePath));
    }
  };
}

async function readJson<T>(zip: Zip, name: string): Promise<T> {
  const content = await zip.read(name);
  try {
    return JSON.parse(decoder.decode(content)) as T;
  } catch {
    throw new TraceOpenError("corrupt", `${name} is not valid JSON.`);
  }
}
