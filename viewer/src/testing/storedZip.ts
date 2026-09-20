const encoder = new TextEncoder();

const LOCAL_FILE = 0x04034b50;
const CENTRAL_FILE = 0x02014b50;
const EOCD = 0x06054b50;

export interface StoredZipEntry {
  data: string | Uint8Array;
  method?: number;
}

/** Builds the smallest ZIP shape the viewer accepts, without compression or CRC validation. */
export function storedZip(entries: Record<string, string | Uint8Array | StoredZipEntry>): ArrayBuffer {
  const localParts: Uint8Array[] = [];
  const centralParts: Uint8Array[] = [];
  let localOffset = 0;

  for (const [name, value] of Object.entries(entries)) {
    const entry = typeof value === "object" && !(value instanceof Uint8Array) && "data" in value
      ? value
      : { data: value };
    const nameBytes = encoder.encode(name);
    const data = typeof entry.data === "string" ? encoder.encode(entry.data) : entry.data;
    const method = entry.method ?? 0;
    const local = join(
      u32(LOCAL_FILE), u16(20), u16(0), u16(method), u16(0), u16(0), u32(0),
      u32(data.length), u32(data.length), u16(nameBytes.length), u16(0), nameBytes, data);
    localParts.push(local);
    centralParts.push(join(
      u32(CENTRAL_FILE), u16(20), u16(20), u16(0), u16(method), u16(0), u16(0), u32(0),
      u32(data.length), u32(data.length), u16(nameBytes.length), u16(0), u16(0), u16(0),
      u16(0), u32(0), u32(localOffset), nameBytes));
    localOffset += local.length;
  }

  const central = join(...centralParts);
  const end = join(
    u32(EOCD), u16(0), u16(0), u16(centralParts.length), u16(centralParts.length),
    u32(central.length), u32(localOffset), u16(0));
  const archive = join(...localParts, central, end);
  return archive.buffer.slice(archive.byteOffset, archive.byteOffset + archive.byteLength) as ArrayBuffer;
}

function u16(value: number): Uint8Array {
  const bytes = new Uint8Array(2);
  new DataView(bytes.buffer).setUint16(0, value, true);
  return bytes;
}

function u32(value: number): Uint8Array {
  const bytes = new Uint8Array(4);
  new DataView(bytes.buffer).setUint32(0, value, true);
  return bytes;
}

function join(...parts: Uint8Array[]): Uint8Array {
  const result = new Uint8Array(parts.reduce((length, part) => length + part.length, 0));
  let offset = 0;
  for (const part of parts) {
    result.set(part, offset);
    offset += part.length;
  }
  return result;
}
