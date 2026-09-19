import type { WireManifest, WireSpans, WireState } from "./wire";

const decoder = new TextDecoder();
const EOCD = 0x06054b50;
const CENTRAL_FILE = 0x02014b50;
const LOCAL_FILE = 0x04034b50;
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

interface ZipEntry {
  method: number;
  compressedSize: number;
  uncompressedSize: number;
  localOffset: number;
}

export interface TraceArchive {
  spans: WireSpans;
  state: WireState;
  /** Reads a file the trace declared as an artifact; anything else in the ZIP stays closed. */
  readFile(archivePath: string, mediaType: string): Promise<Blob>;
  /** Reads a source file the manifest embedded for a code.file.path, or undefined when it was not embedded. */
  readSource(path: string): Promise<string | undefined>;
}

export async function openTraceArchive(buffer: ArrayBuffer): Promise<TraceArchive> {
  if (buffer.byteLength > MAX_ARCHIVE_BYTES) throw new TraceOpenError("unsupported", "This trace is larger than the 250 MiB the viewer reads.");
  const bytes = new Uint8Array(buffer);
  const view = new DataView(buffer);
  const entries = readDirectory(bytes, view);
  const manifest = await readJson<WireManifest>(bytes, view, entries, "manifest.json");
  if (!manifest.spansEntry || !manifest.stateEntry) {
    if (manifest.runEntry) throw new TraceOpenError("legacy", "This trace was written by an older ProtoTest version.");
    throw new TraceOpenError("corrupt", "The trace manifest names no documents.");
  }
  const spans = await readJson<WireSpans>(bytes, view, entries, manifest.spansEntry);
  const state = await readJson<WireState>(bytes, view, entries, manifest.stateEntry);
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
      const content = await readEntry(bytes, view, entries, archivePath);
      const copy = content.buffer.slice(content.byteOffset, content.byteOffset + content.byteLength) as ArrayBuffer;
      return new Blob([copy], { type: mediaType });
    },
    async readSource(path) {
      const archivePath = manifest.sources?.[path];
      if (!archivePath || !entries.has(archivePath)) return undefined;
      return decoder.decode(await readEntry(bytes, view, entries, archivePath));
    }
  };
}

async function readJson<T>(bytes: Uint8Array, view: DataView, entries: Map<string, ZipEntry>, name: string): Promise<T> {
  const content = await readEntry(bytes, view, entries, name);
  try {
    return JSON.parse(decoder.decode(content)) as T;
  } catch {
    throw new TraceOpenError("corrupt", `${name} is not valid JSON.`);
  }
}

function readDirectory(bytes: Uint8Array, view: DataView): Map<string, ZipEntry> {
  let eocd = -1;
  for (let offset = bytes.length - 22; offset >= Math.max(0, bytes.length - 65_557); offset--) {
    if (view.getUint32(offset, true) === EOCD) { eocd = offset; break; }
  }
  if (eocd < 0) throw new TraceOpenError("corrupt", "This file is not a ProtoTrace archive.");
  const count = view.getUint16(eocd + 10, true);
  if (count > MAX_ENTRIES) throw new TraceOpenError("unsupported", "This trace contains too many files.");
  let offset = view.getUint32(eocd + 16, true);
  const entries = new Map<string, ZipEntry>();
  for (let index = 0; index < count; index++) {
    if (view.getUint32(offset, true) !== CENTRAL_FILE) throw new TraceOpenError("corrupt", "The trace's ZIP directory is damaged.");
    const nameLength = view.getUint16(offset + 28, true);
    const extraLength = view.getUint16(offset + 30, true);
    const commentLength = view.getUint16(offset + 32, true);
    const name = decoder.decode(bytes.subarray(offset + 46, offset + 46 + nameLength));
    entries.set(name, {
      method: view.getUint16(offset + 10, true),
      compressedSize: view.getUint32(offset + 20, true),
      uncompressedSize: view.getUint32(offset + 24, true),
      localOffset: view.getUint32(offset + 42, true)
    });
    offset += 46 + nameLength + extraLength + commentLength;
  }
  return entries;
}

async function readEntry(bytes: Uint8Array, view: DataView, entries: Map<string, ZipEntry>, name: string): Promise<Uint8Array> {
  const entry = entries.get(name);
  if (!entry) throw new TraceOpenError("corrupt", `The trace is missing ${name}.`);
  if (entry.uncompressedSize > MAX_ARCHIVE_BYTES) throw new TraceOpenError("unsupported", `${name} is too large to read.`);
  const offset = entry.localOffset;
  if (view.getUint32(offset, true) !== LOCAL_FILE) throw new TraceOpenError("corrupt", `${name} is damaged.`);
  const start = offset + 30 + view.getUint16(offset + 26, true) + view.getUint16(offset + 28, true);
  const compressed = bytes.slice(start, start + entry.compressedSize);
  if (entry.method === 0) return compressed;
  if (entry.method !== 8 || typeof DecompressionStream === "undefined")
    throw new TraceOpenError("unsupported", "This trace uses a ZIP compression this browser cannot read.");

  // Stream the inflation so a header that understates the size cannot decompress past the cap, and
  // turn any decompression failure into the designed corrupt state instead of a raw runtime error.
  const reader = new Blob([compressed]).stream()
    .pipeThrough(new DecompressionStream("deflate-raw"))
    .getReader();
  const chunks: Uint8Array[] = [];
  let total = 0;
  try {
    for (;;) {
      const { done, value } = await reader.read();
      if (done) break;
      total += value.byteLength;
      if (total > MAX_ARCHIVE_BYTES) {
        await reader.cancel();
        throw new TraceOpenError("unsupported", `${name} is too large to read.`);
      }
      chunks.push(value);
    }
  } catch (error) {
    if (error instanceof TraceOpenError) throw error;
    throw new TraceOpenError("corrupt", `${name} could not be decompressed.`);
  }
  const decompressed = new Uint8Array(total);
  let written = 0;
  for (const chunk of chunks) {
    decompressed.set(chunk, written);
    written += chunk.byteLength;
  }
  return decompressed;
}
