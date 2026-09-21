const decoder = new TextDecoder();
const EOCD = 0x06054b50;
const CENTRAL_FILE = 0x02014b50;
const LOCAL_FILE = 0x04034b50;
const MAX_WORKBOOK_BYTES = 50 * 1024 * 1024;
const MAX_ENTRIES = 10_000;
const MAX_PREVIEW_ROWS = 500;
const MAX_PREVIEW_COLUMNS = 100;
const MAX_PREVIEW_CELLS = 50_000;

interface ZipEntry {
  method: number;
  compressedSize: number;
  uncompressedSize: number;
  localOffset: number;
}

export interface WorkbookSheet {
  name: string;
  hidden: boolean;
  rows: string[][];
  rowCount: number;
  columnCount: number;
  truncated: boolean;
}

export interface WorkbookPreview {
  sheets: WorkbookSheet[];
}

export async function readWorkbook(blob: Blob): Promise<WorkbookPreview> {
  if (blob.size > MAX_WORKBOOK_BYTES) throw new Error("This workbook is too large to preview (50 MiB maximum).");
  const buffer = await blob.arrayBuffer();
  const bytes = new Uint8Array(buffer);
  const view = new DataView(buffer);
  const entries = readDirectory(bytes, view);
  const workbook = parseXml(await readText(bytes, view, entries, "xl/workbook.xml"), "workbook.xml");
  const relationships = parseXml(
    await readText(bytes, view, entries, "xl/_rels/workbook.xml.rels"),
    "workbook relationships");
  const sharedStrings = entries.has("xl/sharedStrings.xml")
    ? readSharedStrings(parseXml(await readText(bytes, view, entries, "xl/sharedStrings.xml"), "shared strings"))
    : [];
  const dateStyles = entries.has("xl/styles.xml")
    ? readDateStyles(parseXml(await readText(bytes, view, entries, "xl/styles.xml"), "styles"))
    : new Set<number>();

  const targets = new Map<string, string>();
  for (const relation of elements(relationships, "Relationship")) {
    const id = relation.getAttribute("Id");
    const target = relation.getAttribute("Target");
    if (id && target) targets.set(id, workbookPath(target));
  }

  const sheets: WorkbookSheet[] = [];
  for (const sheet of elements(workbook, "sheet")) {
    const id = sheet.getAttribute("r:id")
      ?? sheet.getAttributeNS("http://schemas.openxmlformats.org/officeDocument/2006/relationships", "id");
    const target = id ? targets.get(id) : undefined;
    if (!target || !entries.has(target)) continue;
    const document = parseXml(await readText(bytes, view, entries, target), `${sheet.getAttribute("name") ?? "worksheet"}.xml`);
    sheets.push(readSheet(
      document,
      sheet.getAttribute("name") ?? `Sheet ${sheets.length + 1}`,
      sheet.getAttribute("state") !== null && sheet.getAttribute("state") !== "visible",
      sharedStrings,
      dateStyles));
  }

  if (!sheets.length) throw new Error("The workbook contains no readable worksheets.");
  return { sheets };
}

function readSheet(
  document: XMLDocument,
  name: string,
  hidden: boolean,
  sharedStrings: string[],
  dateStyles: Set<number>): WorkbookSheet {
  const values = new Map<string, string>();
  let maxRow = -1;
  let maxColumn = -1;
  let seen = 0;
  let truncated = false;

  for (const cell of elements(document, "c")) {
    const reference = cell.getAttribute("r") ?? "";
    const position = cellPosition(reference);
    if (!position) continue;
    maxRow = Math.max(maxRow, position.row);
    maxColumn = Math.max(maxColumn, position.column);
    if (position.row >= MAX_PREVIEW_ROWS || position.column >= MAX_PREVIEW_COLUMNS || seen >= MAX_PREVIEW_CELLS) {
      truncated = true;
      continue;
    }
    values.set(`${position.row}:${position.column}`, cellValue(cell, sharedStrings, dateStyles));
    seen++;
  }

  const rowCount = maxRow + 1;
  const columnCount = maxColumn + 1;
  if (rowCount > MAX_PREVIEW_ROWS || columnCount > MAX_PREVIEW_COLUMNS) truncated = true;
  const visibleRows = Math.min(rowCount, MAX_PREVIEW_ROWS);
  const visibleColumns = Math.min(columnCount, MAX_PREVIEW_COLUMNS);
  const rows = Array.from({ length: visibleRows }, (_, row) =>
    Array.from({ length: visibleColumns }, (_, column) => values.get(`${row}:${column}`) ?? ""));
  return { name, hidden, rows, rowCount, columnCount, truncated };
}

function cellValue(cell: Element, sharedStrings: string[], dateStyles: Set<number>): string {
  const type = cell.getAttribute("t");
  if (type === "inlineStr") return textNodes(cell);
  const raw = elements(cell, "v")[0]?.textContent ?? "";
  if (type === "s") return sharedStrings[Number(raw)] ?? raw;
  if (type === "b") return raw === "1" ? "TRUE" : "FALSE";
  if (type === "e") return `#${raw}`;
  const style = Number(cell.getAttribute("s") ?? "-1");
  if (raw && dateStyles.has(style)) return excelDate(raw);
  return raw;
}

function readSharedStrings(document: XMLDocument): string[] {
  return elements(document, "si").map(textNodes);
}

function readDateStyles(document: XMLDocument): Set<number> {
  const dateFormats = new Set([14, 15, 16, 17, 18, 19, 20, 21, 22, 45, 46, 47]);
  for (const format of elements(document, "numFmt")) {
    const id = Number(format.getAttribute("numFmtId"));
    const code = (format.getAttribute("formatCode") ?? "")
      .replace(/\[[^\]]*]/g, "")
      .replace(/"[^"]*"/g, "");
    if (/[ymdhis]/i.test(code)) dateFormats.add(id);
  }
  const styles = new Set<number>();
  const cellFormats = elements(document, "cellXfs")[0];
  if (!cellFormats) return styles;
  Array.from(cellFormats.children).forEach((format, index) => {
    if (dateFormats.has(Number(format.getAttribute("numFmtId")))) styles.add(index);
  });
  return styles;
}

function excelDate(raw: string): string {
  const serial = Number(raw);
  if (!Number.isFinite(serial)) return raw;
  const date = new Date(Date.UTC(1899, 11, 30) + serial * 86_400_000);
  if (Number.isNaN(date.getTime())) return raw;
  return serial % 1 === 0 ? date.toISOString().slice(0, 10) : date.toISOString().replace(".000Z", "Z");
}

function cellPosition(reference: string): { row: number; column: number } | undefined {
  const match = /^([A-Z]+)(\d+)$/i.exec(reference);
  if (!match) return undefined;
  let column = 0;
  for (const character of match[1].toUpperCase()) column = column * 26 + character.charCodeAt(0) - 64;
  return { row: Number(match[2]) - 1, column: column - 1 };
}

export function columnName(index: number): string {
  let value = index + 1;
  let name = "";
  while (value > 0) {
    value--;
    name = String.fromCharCode(65 + value % 26) + name;
    value = Math.floor(value / 26);
  }
  return name;
}

function textNodes(element: Element): string {
  return elements(element, "t").map(node => node.textContent ?? "").join("");
}

function elements(document: Document | Element, localName: string): Element[] {
  return Array.from(document.getElementsByTagNameNS("*", localName));
}

function parseXml(xml: string, label: string): XMLDocument {
  const document = new DOMParser().parseFromString(xml, "application/xml");
  if (document.querySelector("parsererror")) throw new Error(`The workbook's ${label} is not valid XML.`);
  return document;
}

function workbookPath(target: string): string {
  const source = target.replaceAll("\\", "/");
  const path = source.startsWith("/") ? source.slice(1) : `xl/${source}`;
  const parts: string[] = [];
  for (const part of path.split("/")) {
    if (!part || part === ".") continue;
    if (part === "..") parts.pop();
    else parts.push(part);
  }
  return parts.join("/");
}

async function readText(bytes: Uint8Array, view: DataView, entries: Map<string, ZipEntry>, name: string): Promise<string> {
  return decoder.decode(await readEntry(bytes, view, entries, name));
}

function readDirectory(bytes: Uint8Array, view: DataView): Map<string, ZipEntry> {
  let eocd = -1;
  for (let offset = bytes.length - 22; offset >= Math.max(0, bytes.length - 65_557); offset--) {
    if (view.getUint32(offset, true) === EOCD) { eocd = offset; break; }
  }
  if (eocd < 0) throw new Error("This artifact is not a readable .xlsx workbook.");
  const count = view.getUint16(eocd + 10, true);
  if (count > MAX_ENTRIES) throw new Error("This workbook contains too many files to preview.");
  let offset = view.getUint32(eocd + 16, true);
  const entries = new Map<string, ZipEntry>();
  for (let index = 0; index < count; index++) {
    if (!contains(bytes, offset, 46)) throw new Error("The workbook ZIP directory is damaged.");
    if (view.getUint32(offset, true) !== CENTRAL_FILE) throw new Error("The workbook ZIP directory is damaged.");
    const nameLength = view.getUint16(offset + 28, true);
    const extraLength = view.getUint16(offset + 30, true);
    const commentLength = view.getUint16(offset + 32, true);
    const recordLength = 46 + nameLength + extraLength + commentLength;
    if (!contains(bytes, offset, recordLength)) throw new Error("The workbook ZIP directory is damaged.");
    const name = decoder.decode(bytes.subarray(offset + 46, offset + 46 + nameLength));
    if (entries.has(name)) throw new Error(`The workbook contains duplicate entries named ${name}.`);
    entries.set(name, {
      method: view.getUint16(offset + 10, true),
      compressedSize: view.getUint32(offset + 20, true),
      uncompressedSize: view.getUint32(offset + 24, true),
      localOffset: view.getUint32(offset + 42, true)
    });
    offset += recordLength;
  }
  return entries;
}

async function readEntry(bytes: Uint8Array, view: DataView, entries: Map<string, ZipEntry>, name: string): Promise<Uint8Array> {
  const entry = entries.get(name);
  if (!entry) throw new Error(`The workbook is missing ${name}.`);
  if (entry.uncompressedSize > MAX_WORKBOOK_BYTES) throw new Error(`${name} is too large to preview.`);
  const offset = entry.localOffset;
  if (!contains(bytes, offset, 30)) throw new Error(`${name} is damaged.`);
  if (view.getUint32(offset, true) !== LOCAL_FILE) throw new Error(`${name} is damaged.`);
  const start = offset + 30 + view.getUint16(offset + 26, true) + view.getUint16(offset + 28, true);
  if (!contains(bytes, start, entry.compressedSize)) throw new Error(`${name} is damaged.`);
  const compressed = bytes.slice(start, start + entry.compressedSize);
  if (entry.method === 0) {
    if (compressed.byteLength !== entry.uncompressedSize) throw new Error(`${name} has an invalid size.`);
    return compressed;
  }
  if (entry.method !== 8 || typeof DecompressionStream === "undefined")
    throw new Error("This browser cannot decompress the workbook.");
  const reader = new Blob([compressed]).stream()
    .pipeThrough(new DecompressionStream("deflate-raw"))
    .getReader();
  const chunks: Uint8Array[] = [];
  let total = 0;
  const tooLargeError = new Error(`${name} is too large to preview.`);
  try {
    for (;;) {
      const { done, value } = await reader.read();
      if (done) break;
      total += value.byteLength;
      if (total > MAX_WORKBOOK_BYTES) {
        await reader.cancel();
        throw tooLargeError;
      }
      chunks.push(value);
    }
  } catch (error) {
    if (error === tooLargeError) throw error;
    throw new Error(`${name} could not be decompressed.`);
  }
  if (total !== entry.uncompressedSize) throw new Error(`${name} has an invalid size.`);
  const decompressed = new Uint8Array(total);
  let written = 0;
  for (const chunk of chunks) {
    decompressed.set(chunk, written);
    written += chunk.byteLength;
  }
  return decompressed;
}

function contains(bytes: Uint8Array, offset: number, length: number): boolean {
  return Number.isSafeInteger(offset)
    && Number.isSafeInteger(length)
    && offset >= 0
    && length >= 0
    && offset <= bytes.length
    && length <= bytes.length - offset;
}
