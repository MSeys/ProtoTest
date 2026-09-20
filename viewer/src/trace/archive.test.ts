import { describe, expect, it } from "vitest";
import { storedZip } from "../testing/storedZip";
import { openTraceArchive, TraceOpenError } from "./archive";

describe("openTraceArchive", () => {
  it("opens the current wire and exposes only declared artifacts and sources", async () => {
    const archive = await openTraceArchive(traceZip({
      "artifacts/result.txt": "created",
      "sources/Orders.cs": "public sealed class Orders {}"
    }, {
      artifacts: [{ archivePath: "artifacts/result.txt" }],
      sources: { "C:/src/Orders.cs": "sources/Orders.cs" }
    }));

    expect(archive.spans.formatVersion).toBe("2.0");
    expect(await archive.readSource("C:/src/Orders.cs")).toBe("public sealed class Orders {}");
    expect(await archive.readSource("C:/src/Missing.cs")).toBeUndefined();
    expect(await (await archive.readFile("artifacts/result.txt", "text/plain")).text()).toBe("created");
    await expect(archive.readFile("sources/Orders.cs", "text/plain"))
      .rejects.toThrow("not declared");
  });

  it("classifies non-ZIP and legacy traces", async () => {
    await expect(openTraceArchive(new Uint8Array([1, 2, 3]).buffer))
      .rejects.toMatchObject({ problem: "corrupt" });
    await expect(openTraceArchive(storedZip({
      "manifest.json": JSON.stringify({ runEntry: "run.json" })
    }))).rejects.toMatchObject({ problem: "legacy" });
  });

  it("rejects malformed documents and unsupported wire versions", async () => {
    await expect(openTraceArchive(traceZip({}, {}, "not-json")))
      .rejects.toEqual(expect.objectContaining({ problem: "corrupt", message: expect.stringContaining("not valid JSON") }));
    await expect(openTraceArchive(traceZip({}, { spanVersion: "3.0" })))
      .rejects.toEqual(expect.objectContaining({ problem: "unsupported", message: expect.stringContaining("Span format 3.0") }));
    await expect(openTraceArchive(traceZip({}, { stateVersion: "9.0" })))
      .rejects.toEqual(expect.objectContaining({ problem: "unsupported", message: expect.stringContaining("State format 9.0") }));
  });

  it("turns damaged directory offsets into a designed corrupt error", async () => {
    const bytes = new Uint8Array(traceZip());
    const eocd = bytes.length - 22;
    new DataView(bytes.buffer).setUint32(eocd + 16, bytes.length - 2, true);

    await expect(openTraceArchive(bytes.buffer)).rejects.toBeInstanceOf(TraceOpenError);
    await expect(openTraceArchive(bytes.buffer)).rejects.toMatchObject({ problem: "corrupt" });
  });

  it("rejects an unsupported compression method when the declared artifact is opened", async () => {
    const archive = await openTraceArchive(traceZip({
      "artifacts/result.bin": { data: new Uint8Array([1, 2, 3]), method: 99 }
    }, { artifacts: [{ archivePath: "artifacts/result.bin" }] }));

    await expect(archive.readFile("artifacts/result.bin", "application/octet-stream"))
      .rejects.toMatchObject({ problem: "unsupported" });
  });
});

function traceZip(
  extra: Record<string, string | Uint8Array | { data: string | Uint8Array; method?: number }> = {},
  options: {
    artifacts?: Array<{ archivePath: string }>;
    sources?: Record<string, string>;
    spanVersion?: string;
    stateVersion?: string;
  } = {},
  spansJson?: string): ArrayBuffer {
  return storedZip({
    "manifest.json": JSON.stringify({
      spansEntry: "spans.json",
      stateEntry: "state.json",
      sources: options.sources
    }),
    "spans.json": spansJson ?? JSON.stringify({
      formatVersion: options.spanVersion ?? "2.0",
      resourceSpans: [{ artifacts: options.artifacts ?? [] }]
    }),
    "state.json": JSON.stringify({ formatVersion: options.stateVersion ?? "2.0" }),
    ...extra
  });
}
