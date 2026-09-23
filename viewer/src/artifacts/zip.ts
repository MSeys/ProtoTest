const decoder = new TextDecoder();
const EOCD = 0x06054b50;
const CENTRAL_FILE = 0x02014b50;
const LOCAL_FILE = 0x04034b50;

export interface ZipEntry {
  method: number;
  compressedSize: number;
  uncompressedSize: number;
  localOffset: number;
}

export type ZipProblemKind = "corrupt" | "unsupported";

/** The caller's messages, so one reader serves the trace archive and the workbook preview. */
export interface ZipVocabulary {
  notArchive: string;
  tooManyEntries: string;
  directoryDamaged: string;
  duplicate: (name: string) => string;
  missing: (name: string) => string;
  tooLarge: (name: string) => string;
  damaged: (name: string) => string;
  invalidSize: (name: string) => string;
  unsupportedCompression: string;
  decompressionFailed: (name: string) => string;
}

export interface ZipOptions {
  maxEntries: number;
  maxBytes: number;
  /** Builds the caller's error type, so a trace reads as a `TraceOpenError` and a workbook as an `Error`. */
  createError: (kind: ZipProblemKind, message: string) => Error;
  vocabulary: ZipVocabulary;
}

export interface Zip {
  entries: Map<string, ZipEntry>;
  /** Reads one entry, inflating it when needed and enforcing the byte cap while streaming. */
  read(name: string): Promise<Uint8Array>;
}

/**
 * Opens the subset of ZIP the viewer needs: a central directory, stored or deflate-raw entries, and
 * hard caps on the entry count and the decompressed size. Every failure is reported through the
 * caller's error factory, so both callers keep their own designed states.
 */
export function openZip(bytes: Uint8Array, options: ZipOptions): Zip {
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const fail = (kind: ZipProblemKind, message: string): never => {
    throw options.createError(kind, message);
  };
  const entries = readDirectory();
  return { entries, read: name => readEntry(name) };

  function readDirectory(): Map<string, ZipEntry> {
    let eocd = -1;
    for (let offset = bytes.length - 22; offset >= Math.max(0, bytes.length - 65_557); offset--) {
      if (view.getUint32(offset, true) === EOCD) { eocd = offset; break; }
    }
    if (eocd < 0) fail("corrupt", options.vocabulary.notArchive);
    const count = view.getUint16(eocd + 10, true);
    if (count > options.maxEntries) fail("unsupported", options.vocabulary.tooManyEntries);
    let offset = view.getUint32(eocd + 16, true);
    const directory = new Map<string, ZipEntry>();
    for (let index = 0; index < count; index++) {
      if (!contains(offset, 46) || view.getUint32(offset, true) !== CENTRAL_FILE)
        fail("corrupt", options.vocabulary.directoryDamaged);
      const nameLength = view.getUint16(offset + 28, true);
      const extraLength = view.getUint16(offset + 30, true);
      const commentLength = view.getUint16(offset + 32, true);
      const recordLength = 46 + nameLength + extraLength + commentLength;
      if (!contains(offset, recordLength)) fail("corrupt", options.vocabulary.directoryDamaged);
      const name = decoder.decode(bytes.subarray(offset + 46, offset + 46 + nameLength));
      if (directory.has(name)) fail("corrupt", options.vocabulary.duplicate(name));
      directory.set(name, {
        method: view.getUint16(offset + 10, true),
        compressedSize: view.getUint32(offset + 20, true),
        uncompressedSize: view.getUint32(offset + 24, true),
        localOffset: view.getUint32(offset + 42, true)
      });
      offset += recordLength;
    }
    return directory;
  }

  async function readEntry(name: string): Promise<Uint8Array> {
    const entry = entries.get(name);
    if (!entry) return fail("corrupt", options.vocabulary.missing(name));
    if (entry.uncompressedSize > options.maxBytes) fail("unsupported", options.vocabulary.tooLarge(name));
    const offset = entry.localOffset;
    if (!contains(offset, 30) || view.getUint32(offset, true) !== LOCAL_FILE)
      fail("corrupt", options.vocabulary.damaged(name));
    const start = offset + 30 + view.getUint16(offset + 26, true) + view.getUint16(offset + 28, true);
    if (!contains(start, entry.compressedSize)) fail("corrupt", options.vocabulary.damaged(name));
    const compressed = bytes.slice(start, start + entry.compressedSize);
    if (entry.method === 0) {
      if (compressed.byteLength !== entry.uncompressedSize) fail("corrupt", options.vocabulary.invalidSize(name));
      return compressed;
    }
    if (entry.method !== 8 || typeof DecompressionStream === "undefined")
      fail("unsupported", options.vocabulary.unsupportedCompression);

    // Stream the inflation so a header that understates the size cannot decompress past the cap, and
    // turn any decompression failure into the designed corrupt state instead of a raw runtime error.
    const reader = new Blob([compressed]).stream()
      .pipeThrough(new DecompressionStream("deflate-raw"))
      .getReader();
    const chunks: Uint8Array[] = [];
    let total = 0;
    const tooLarge = options.createError("unsupported", options.vocabulary.tooLarge(name));
    try {
      for (;;) {
        const { done, value } = await reader.read();
        if (done) break;
        total += value.byteLength;
        if (total > options.maxBytes) {
          await reader.cancel();
          throw tooLarge;
        }
        chunks.push(value);
      }
    } catch (error) {
      if (error === tooLarge) throw error;
      throw options.createError("corrupt", options.vocabulary.decompressionFailed(name));
    }
    const decompressed = new Uint8Array(total);
    let written = 0;
    for (const chunk of chunks) {
      decompressed.set(chunk, written);
      written += chunk.byteLength;
    }
    if (decompressed.byteLength !== entry.uncompressedSize) fail("corrupt", options.vocabulary.invalidSize(name));
    return decompressed;
  }

  function contains(offset: number, length: number): boolean {
    return Number.isSafeInteger(offset)
      && Number.isSafeInteger(length)
      && offset >= 0
      && length >= 0
      && offset <= bytes.length
      && length <= bytes.length - offset;
  }
}
