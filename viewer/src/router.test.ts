import { describe, expect, it } from "vitest";
import { href, parse } from "./router";

// The address names the view and the test, and carries the selection: a link to a failing check shares as one.
describe("router addresses", () => {
  it("names the run", () => {
    expect(href({ name: "run" })).toBe("#/");
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

  it("opens the steps for a view it does not know", () => {
    expect(parse("#/test/t1/nothing")).toMatchObject({ view: "steps" });
  });
});
