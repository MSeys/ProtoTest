import type { TraceArtifact, TraceRun } from "./trace-schema";

const decoder = new TextDecoder();
const EOCD = 0x06054b50;
const CENTRAL_FILE = 0x02014b50;
const LOCAL_FILE = 0x04034b50;
const MAX_ARCHIVE_BYTES = 250 * 1024 * 1024;
const MAX_ENTRIES = 50_000;

interface ZipEntry {
  method: number;
  compressedSize: number;
  uncompressedSize: number;
  localOffset: number;
}

interface TraceManifest {
  formatVersion: string;
  runEntry: string;
}

export interface LoadedTrace {
  run: TraceRun;
  readArtifact(artifact: TraceArtifact): Promise<Blob>;
}

export async function readProtoTrace(arrayBuffer: ArrayBuffer): Promise<LoadedTrace> {
  if (arrayBuffer.byteLength > MAX_ARCHIVE_BYTES) throw new Error("Trace is larger than the 250 MiB viewer limit.");
  const bytes = new Uint8Array(arrayBuffer);
  const view = new DataView(arrayBuffer);
  const entries = readDirectory(bytes, view);
  const manifest = JSON.parse(decoder.decode(await readEntry(bytes, view, entries, "manifest.json"))) as TraceManifest;
  if (!manifest.runEntry || typeof manifest.formatVersion !== "string") throw new Error("Trace manifest is incomplete.");
  if (!manifest.formatVersion.startsWith("1.")) throw new Error(`Trace format ${manifest.formatVersion} is not supported.`);
  const run = JSON.parse(decoder.decode(await readEntry(bytes, view, entries, manifest.runEntry))) as TraceRun;
  return {
    run,
    async readArtifact(artifact) {
      const declaredByRun = run.artifacts?.some(candidate => candidate.archivePath === artifact.archivePath);
      const declaredByTest = run.tests.some(test => test.artifacts?.some(candidate => candidate.archivePath === artifact.archivePath));
      if (!declaredByRun && !declaredByTest)
        throw new Error("The requested artifact is not declared by this trace.");
      const content = await readEntry(bytes, view, entries, artifact.archivePath);
      const buffer = content.buffer.slice(content.byteOffset, content.byteOffset + content.byteLength) as ArrayBuffer;
      return new Blob([buffer], { type: artifact.mediaType });
    }
  };
}

function readDirectory(bytes: Uint8Array, view: DataView): Map<string, ZipEntry> {
  let eocd = -1;
  for (let offset = bytes.length - 22; offset >= Math.max(0, bytes.length - 65_557); offset--) {
    if (view.getUint32(offset, true) === EOCD) { eocd = offset; break; }
  }
  if (eocd < 0) throw new Error("This file is not a valid ProtoTrace archive.");
  const count = view.getUint16(eocd + 10, true);
  if (count > MAX_ENTRIES) throw new Error("Trace contains too many entries.");
  let offset = view.getUint32(eocd + 16, true);
  const entries = new Map<string, ZipEntry>();
  for (let index = 0; index < count; index++) {
    if (view.getUint32(offset, true) !== CENTRAL_FILE) throw new Error("Trace ZIP directory is invalid.");
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
  if (!entry) throw new Error(`Trace entry '${name}' is missing.`);
  if (entry.uncompressedSize > MAX_ARCHIVE_BYTES) throw new Error(`Trace entry '${name}' is too large.`);
  const offset = entry.localOffset;
  if (view.getUint32(offset, true) !== LOCAL_FILE) throw new Error("Trace ZIP entry is invalid.");
  const start = offset + 30 + view.getUint16(offset + 26, true) + view.getUint16(offset + 28, true);
  const compressed = bytes.slice(start, start + entry.compressedSize);
  if (entry.method === 0) return compressed;
  if (entry.method !== 8 || typeof DecompressionStream === "undefined") throw new Error("This trace uses an unsupported ZIP compression method.");
  const stream = new Blob([compressed]).stream().pipeThrough(new DecompressionStream("deflate-raw"));
  return new Uint8Array(await new Response(stream).arrayBuffer());
}
