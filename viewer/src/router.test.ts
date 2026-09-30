import { describe, expect, it } from "vitest";
import { href, parse } from "./router";

// The address names the view and the test, and carries the selection: a link to a failing check shares as one.
describe("router addresses", () => {
  it("names the run", () => {
    expect(href({ name: "run" })).toBe("#/");
  });

  it("round trips run operations and tracked items", () => {
    const operation = { name: "run" as const, selection: { span: "release/1" } };
    const item = { name: "run" as const, selection: { item: { kind: "broker", id: "main bus" } } };
    expect(href(operation)).toBe("#/?span=release%2F1");
    expect(parse(href(operation))).toEqual(operation);
    expect(parse(href(item))).toEqual(item);
  });

  it("names a test view", () => {
    expect(href({ name: "test", testId: "t1", view: "steps" })).toBe("#/test/t1/steps");
  });

  it("carries a span selection", () => {
    expect(href({ name: "test", testId: "t1", view: "steps", selection: { span: "s1" } }))
      .toBe("#/test/t1/steps?span=s1");
  });

  it("carries an item selection", () => {
    expect(href({ name: "test", testId: "t1", view: "state", selection: { item: { kind: "client", id: "c1" } } }))
      .toBe("#/test/t1/state?kind=client&item=c1");
  });

  it("escapes test ids", () => {
    expect(href({ name: "test", testId: "a/b c", view: "steps" })).toBe("#/test/a%2Fb%20c/steps");
  });

  // A link shared before a view was renamed still opens the view it meant.
  it("reads a renamed view's old name", () => {
    expect(parse("#/test/t1/story?span=s1")).toEqual({ name: "test", testId: "t1", view: "steps", selection: { span: "s1" } });
  });

  it("keeps Files links and their selection opening Evidence", () => {
    expect(parse("#/test/t1/files?span=s1")).toEqual({ name: "test", testId: "t1", view: "evidence", selection: { span: "s1" } });
    expect(href({ name: "test", testId: "t1", view: "evidence" })).toBe("#/test/t1/evidence");
  });

  it("opens the steps for a view it does not know", () => {
    expect(parse("#/test/t1/nothing")).toMatchObject({ view: "steps" });
  });
});

// The run's views live in the address too, so a shared link opens the same view; the overview keeps "#/".
describe("run views", () => {
  it("reads and writes a run view with its selection", () => {
    expect(parse("#/run/timeline")).toEqual({ name: "run", view: "timeline" });
    expect(parse("#/run/operations?span=7")).toEqual({ name: "run", view: "operations", selection: { span: "7" } });
    expect(parse("#/?span=7")).toEqual({ name: "run", selection: { span: "7" } });
    expect(parse("#/run/unknown")).toEqual({ name: "run" });
    expect(href({ name: "run", view: "overview" })).toBe("#/");
    expect(href({ name: "run", view: "files" })).toBe("#/run/files");
    expect(href({ name: "run", view: "operations", selection: { span: "7" } })).toBe("#/run/operations?span=7");
  });
});
