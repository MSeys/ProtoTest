import { describe, expect, it, vi } from "vitest";
import { copyText, parseTraceParam, shareUrl, traceNameFromUrl } from "./share";

describe("parseTraceParam", () => {
  it("accepts absolute http(s) URLs and reports the rest", () => {
    expect(parseTraceParam(null)).toEqual({ state: "absent" });
    expect(parseTraceParam("")).toEqual({ state: "absent" });
    expect(parseTraceParam("https://example.com/run.prototrace")).toEqual({
      state: "valid", url: "https://example.com/run.prototrace"
    });
    expect(parseTraceParam("http://localhost:8080/run.prototrace")).toEqual({
      state: "valid", url: "http://localhost:8080/run.prototrace"
    });
    expect(parseTraceParam("demos/full.prototrace")).toEqual({ state: "invalid", value: "demos/full.prototrace" });
    expect(parseTraceParam("/demos/full.prototrace")).toEqual({ state: "invalid", value: "/demos/full.prototrace" });
    expect(parseTraceParam("not a url")).toEqual({ state: "invalid", value: "not a url" });
    expect(parseTraceParam("file:///tmp/run.prototrace")).toEqual({ state: "invalid", value: "file:///tmp/run.prototrace" });
  });
});

describe("traceNameFromUrl", () => {
  it("names the trace after the URL's last segment", () => {
    expect(traceNameFromUrl("https://example.com/traces/run.prototrace")).toBe("run.prototrace");
    expect(traceNameFromUrl("https://example.com/")).toBe("example.com");
    expect(traceNameFromUrl("https://example.com")).toBe("example.com");
  });
});

describe("shareUrl", () => {
  it("shares the full demo under its historic address and keeps the reader's place", () => {
    expect(shareUrl("https://trace.prototest.dev", "/", { kind: "demo", key: "full" }, "#/test/abc/story"))
      .toBe("https://trace.prototest.dev/?demo=1#/test/abc/story");
    expect(shareUrl("https://trace.prototest.dev", "/", { kind: "demo", key: "rest-graphql" }, "#/"))
      .toBe("https://trace.prototest.dev/?demo=rest-graphql#/");
  });

  it("shares a hosted trace as its URL and a local file as nothing", () => {
    expect(shareUrl("https://trace.prototest.dev", "/", { kind: "remote", url: "https://example.com/run.prototrace" }, "#/"))
      .toBe("https://trace.prototest.dev/?trace=https%3A%2F%2Fexample.com%2Frun.prototrace#/");
    expect(shareUrl("https://trace.prototest.dev", "/", { kind: "file" }, "#/")).toBeNull();
  });
});

describe("copyText", () => {
  it("uses the async clipboard when it is there", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    vi.stubGlobal("navigator", { clipboard: { writeText } });

    await expect(copyText("https://trace.prototest.dev/?demo=1")).resolves.toBe(true);
    expect(writeText).toHaveBeenCalledWith("https://trace.prototest.dev/?demo=1");

    vi.unstubAllGlobals();
  });
});
