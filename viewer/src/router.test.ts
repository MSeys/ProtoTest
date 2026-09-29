import { describe, expect, it } from "vitest";
import { href } from "./router";

// The address names the view and the test, and carries the selection: a link to a failing check shares as one.
describe("router addresses", () => {
  it("names the run", () => {
    expect(href({ name: "run" })).toBe("#/");
  });

  it("names a test view", () => {
    expect(href({ name: "test", testId: "t1", view: "story" })).toBe("#/test/t1/story");
  });

  it("carries a span selection", () => {
    expect(href({ name: "test", testId: "t1", view: "story", selection: { span: "s1" } }))
      .toBe("#/test/t1/story?span=s1");
  });

  it("carries an item selection", () => {
    expect(href({ name: "test", testId: "t1", view: "state", selection: { item: { kind: "client", id: "c1" } } }))
      .toBe("#/test/t1/state?kind=client&item=c1");
  });

  it("escapes test ids", () => {
    expect(href({ name: "test", testId: "a/b c", view: "story" })).toBe("#/test/a%2Fb%20c/story");
  });
});
