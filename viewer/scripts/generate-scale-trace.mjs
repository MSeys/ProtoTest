#!/usr/bin/env node
/*
 * Builds a large, realistic .prototrace from the committed demo trace.
 *
 * The demo trace is the fidelity source: its test resource groups carry the real sections, events,
 * attributes and state the framework writes. This script clones each group to reach the requested test
 * count, rewrites ids, names and timestamps, and writes small artifact payloads under the cloned paths.
 * Output is deterministic (same input, same bytes) and is meant to live outside the repository.
 *
 * Usage:
 *   node scripts/generate-scale-trace.mjs [--tests 1200] [--out .perf/scale.prototrace]
 *     [--source public/demos/prototest-demo.prototrace]
 */

import { createHash } from "node:crypto";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { deflateRawSync, inflateRawSync } from "node:zlib";

const here = dirname(fileURLToPath(import.meta.url));
const viewer = resolve(here, "..");

const options = parse(process.argv.slice(2));
const source = resolve(viewer, options.source ?? "public/demos/prototest-demo.prototrace");
const out = resolve(viewer, options.out ?? ".perf/scale.prototrace");
const wanted = Number(options.tests ?? 1200);

const started = Date.now();
const demo = readZip(readFileSync(source));
const manifest = json(demo, "manifest.json");
const spans = json(demo, manifest.spansEntry);
const state = json(demo, manifest.stateEntry);

const groups = spans.resourceSpans.filter(group => "testId" in group.resource.attributes);
const run = spans.resourceSpans.find(group => !("testId" in group.resource.attributes));
if (!groups.length || !run) throw new Error("The source trace has no test or run resource group.");

const runArtifacts = run.artifacts ?? [];
const report = demo.get(runArtifacts.find(artifact => artifact.name === "report.json")?.archivePath ?? "");
const copies = Math.ceil(wanted / groups.length);
const files = new Map();
const testGroups = [];
const testStates = [];
const runStart = Date.UTC(2026, 8, 20, 10, 32, 18);
// Some overlap, like a parallel suite: a fixed pitch with a small deterministic jitter.
const pitchMs = 40;
let maxEnd = 0;
let index = 0;

for (let copy = 0; copy < copies; copy++) {
  for (let position = 0; position < groups.length && index < wanted; position++, index++) {
    const template = groups[position];
    const attributes = template.resource.attributes;
    const number = String(index + 1).padStart(4, "0");
    const testClass = `ProtoTest.Scale.Journey${String(copy + 1).padStart(2, "0")}`;
    const testName = `${testClass}.${attributes.testMethod}`;
    const testId = `scale-${number}`;
    const start = runStart + index * pitchMs + (index % 5) * 7;

    const shift = shiftOf(template, start);
    const built = cloneGroup(template, files, index, testId, testName, testClass, shift);
    maxEnd = Math.max(maxEnd, start + (attributes.testDurationMs ?? 0));
    testGroups.push(built.group);
    testStates.push({
      testId,
      name: testName,
      items: built.stateItems
    });
  }
}

// The run resource keeps its own artifacts and events; only its identity and window move.
run.resource.attributes = {
  ...run.resource.attributes,
  runId: runId(),
  runStartedAtUtc: iso(runStart),
  runCompletedAtUtc: iso(maxEnd + 5)
};
for (const artifact of runArtifacts) {
  const bytes = artifact.name === "report.json" && report ? report : placeholder(artifact);
  files.set(artifact.archivePath, bytes);
}

const output = { formatVersion: spans.formatVersion, resourceSpans: [...testGroups, run] };
const stateOutput = { formatVersion: state.formatVersion, run: state.run, tests: testStates };
const manifestOutput = {
  formatVersion: manifest.formatVersion,
  spansEntry: manifest.spansEntry,
  stateEntry: manifest.stateEntry,
  sources: manifest.sources
};

for (const [path, bytes] of demo) {
  if (path.endsWith(".json")) continue;
  if (path.startsWith("resources/run/")) files.set(path, bytes);
  else if (path.startsWith("sources/")) files.set(path, bytes);
}

const spansJson = JSON.stringify(output);
const stateJson = JSON.stringify(stateOutput);
const archive = writeZip([
  ["manifest.json", JSON.stringify(manifestOutput)],
  ["spans.json", spansJson],
  ["state.json", stateJson],
  ...[...files.entries()].map(([name, bytes]) => [name, bytes])
]);

mkdirSync(dirname(out), { recursive: true });
writeFileSync(out, archive);
console.log(
  `[scale] tests=${testGroups.length} copies=${copies} zip=${(archive.length / 1024 / 1024).toFixed(2)} MB ` +
  `spans=${(spansJson.length / 1024 / 1024).toFixed(1)} MB ` +
  `state=${(stateJson.length / 1024 / 1024).toFixed(1)} MB ` +
  `generated=${Date.now() - started} ms -> ${out}`
);

/** The delta that moves a template group's own clock to its slot in the generated run. */
function shiftOf(group, start) {
  let earliest = Infinity;
  for (const scope of group.scopeSpans ?? []) {
    for (const span of scope.spans ?? []) earliest = Math.min(earliest, Date.parse(span.startedAtUtc));
    for (const event of scope.events ?? []) earliest = Math.min(earliest, Date.parse(event.atUtc));
  }
  return start - earliest;
}

function cloneGroup(template, files, index, testId, testName, testClass, shift) {
  const group = structuredClone(template);
  const attributes = group.resource.attributes;
  attributes.testId = testId;
  attributes.testName = testName;
  attributes.testClass = testClass;

  const renamed = new Map();
  const artifactIds = new Map();
  for (const artifact of group.artifacts ?? []) {
    const id = `a${index}-${artifact.id}`;
    artifactIds.set(artifact.id, id);
    artifact.id = id;
    artifact.archivePath = `resources/${index}/${id}`;
    const bytes = placeholder(artifact);
    artifact.sizeBytes = bytes.length;
    artifact.error = null;
    files.set(artifact.archivePath, bytes);
  }
  for (const scope of group.scopeSpans ?? []) {
    for (const span of scope.spans ?? []) {
      renamed.set(span.spanId, `s${index}-${span.spanId}`);
      span.spanId = renamed.get(span.spanId);
      span.startedAtUtc = iso(Date.parse(span.startedAtUtc) + shift);
      for (const event of span.events ?? []) {
        event.atUtc = iso(Date.parse(event.atUtc) + shift);
        if (event.artifactId) event.artifactId = artifactIds.get(event.artifactId) ?? null;
      }
    }
    for (const span of scope.spans ?? []) {
      if (span.parentSpanId) span.parentSpanId = renamed.get(span.parentSpanId) ?? null;
    }
    for (const event of scope.events ?? []) event.atUtc = iso(Date.parse(event.atUtc) + shift);
  }

  const templateState = state.tests.find(entry => entry.testId === template.resource.attributes.testId);
  const items = structuredClone(templateState?.items ?? []);
  for (const item of items) {
    item.scope = testName;
    item.firstSeenUtc = iso(Date.parse(item.firstSeenUtc) + shift);
    item.lastSeenUtc = iso(Date.parse(item.lastSeenUtc) + shift);
    for (const change of item.changes ?? []) {
      change.atUtc = iso(Date.parse(change.atUtc) + shift);
      change.operationId = change.operationId ? renamed.get(change.operationId) ?? null : null;
    }
  }

  return { group, stateItems: items };
}

function placeholder(artifact) {
  const media = artifact.mediaType ?? "";
  if (media.includes("json")) {
    return new TextEncoder().encode(JSON.stringify({
      generated: "scale",
      artifact: artifact.name,
      note: "Payload placeholder written by scripts/generate-scale-trace.mjs."
    }, null, 1));
  }
  if (media.includes("image")) {
    // A valid 1x1 PNG, so an image artifact still opens as an image.
    const png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
    return Buffer.from(png, "base64");
  }
  return new TextEncoder().encode(`${artifact.name}\nArtifact placeholder written by scripts/generate-scale-trace.mjs.\n`);
}

function json(entries, name) {
  const entry = entries.get(name);
  if (!entry) throw new Error(`The source trace is missing ${name}.`);
  return JSON.parse(new TextDecoder().decode(entry));
}

function iso(ms) {
  return new Date(ms).toISOString();
}

function runId() {
  return createHash("sha256").update(`scale-trace-${wanted}`).digest("hex").slice(0, 32);
}

/** A minimal ZIP reader: stored and deflate-raw entries, the same subset the viewer reads. */
function readZip(buffer) {
  const view = new DataView(buffer.buffer, buffer.byteOffset, buffer.byteLength);
  let eocd = -1;
  for (let offset = buffer.length - 22; offset >= Math.max(0, buffer.length - 65_557); offset--) {
    if (view.getUint32(offset, true) === 0x06054b50) { eocd = offset; break; }
  }
  if (eocd < 0) throw new Error("Not a ZIP archive.");
  const count = view.getUint16(eocd + 10, true);
  let offset = view.getUint32(eocd + 16, true);
  const entries = new Map();
  for (let entry = 0; entry < count; entry++) {
    const method = view.getUint16(offset + 10, true);
    const compressedSize = view.getUint32(offset + 20, true);
    const nameLength = view.getUint16(offset + 28, true);
    const extraLength = view.getUint16(offset + 30, true);
    const commentLength = view.getUint16(offset + 32, true);
    const localOffset = view.getUint32(offset + 42, true);
    const name = new TextDecoder().decode(new Uint8Array(buffer.buffer, buffer.byteOffset + offset + 46, nameLength));
    const start = localOffset + 30 + view.getUint16(localOffset + 26, true) + view.getUint16(localOffset + 28, true);
    const compressed = new Uint8Array(buffer.buffer, buffer.byteOffset + start, compressedSize);
    entries.set(name, method === 0 ? compressed.slice() : new Uint8Array(inflateRawSync(compressed)));
    offset += 46 + nameLength + extraLength + commentLength;
  }
  return entries;
}

/** A minimal ZIP writer: deflate-raw entries with CRC32, readable by the viewer and by unzip tools. */
function writeZip(entries) {
  const parts = [];
  const central = [];
  const encoder = new TextEncoder();
  let localOffset = 0;

  for (const [name, value] of entries) {
    const nameBytes = encoder.encode(name);
    const raw = typeof value === "string" ? encoder.encode(value) : new Uint8Array(value);
    const data = raw.length > 256 ? deflateRawSync(raw) : raw;
    const method = raw.length > 256 ? 8 : 0;
    const crc = crc32(raw);
    const local = join(
      u32(0x04034b50), u16(20), u16(0), u16(method), u16(0), u16(0), u32(crc),
      u32(data.length), u32(raw.length), u16(nameBytes.length), u16(0), nameBytes, data);
    parts.push(local);
    central.push(join(
      u32(0x02014b50), u16(20), u16(20), u16(0), u16(method), u16(0), u16(0), u32(crc),
      u32(data.length), u32(raw.length), u16(nameBytes.length), u16(0), u16(0), u16(0),
      u16(0), u32(0), u32(localOffset), nameBytes));
    localOffset += local.length;
  }

  const directory = join(...central);
  const end = join(
    u32(0x06054b50), u16(0), u16(0), u16(central.length), u16(central.length),
    u32(directory.length), u32(localOffset), u16(0));
  const archive = join(...parts, directory, end);
  return Buffer.from(archive.buffer, archive.byteOffset, archive.byteLength);
}

function u16(value) {
  const bytes = Buffer.alloc(2);
  bytes.writeUInt16LE(value, 0);
  return bytes;
}

function u32(value) {
  const bytes = Buffer.alloc(4);
  bytes.writeUInt32LE(value >>> 0, 0);
  return bytes;
}

function join(...parts) {
  return Buffer.concat(parts);
}

var crcTable;
function crc32(bytes) {
  crcTable ??= (() => {
    const table = new Uint32Array(256);
    for (let n = 0; n < 256; n++) {
      let c = n;
      for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
      table[n] = c >>> 0;
    }
    return table;
  })();
  let crc = 0xffffffff;
  for (const byte of bytes) crc = crcTable[(crc ^ byte) & 0xff] ^ (crc >>> 8);
  return (crc ^ 0xffffffff) >>> 0;
}

function parse(args) {
  const parsed = {};
  for (let i = 0; i < args.length; i += 2) {
    const key = args[i]?.replace(/^--/, "");
    if (key) parsed[key] = args[i + 1];
  }
  return parsed;
}
