import { describe, expect, it } from "vitest";
import { demos, formatDemoFacts, resolveDemo, summarizeDemo } from "./demos";
import { storedZip } from "./testing/storedZip";

// Adding a demo stays a one-entry operation: the registry answers every question the UI asks.
describe("demos", () => {
  it("resolves the historic ?demo=1 to the full demo and unknown keys to it too", () => {
    expect(resolveDemo("1").key).toBe("full");
    expect(resolveDemo("full").key).toBe("full");
    expect(resolveDemo("rest-graphql").key).toBe("rest-graphql");
    expect(resolveDemo("no-such-demo").key).toBe("full");
    expect(resolveDemo(null).key).toBe("full");
  });

  it("keeps one entry per bundled file", () => {
    expect(demos.map(entry => entry.key)).toEqual(["full", "opencsms", "rest-graphql", "rest-database", "workbook"]);
    for (const entry of demos) {
      expect(entry.file).toMatch(/\.prototrace$/);
      expect(entry.label).toBeTruthy();
      expect(entry.description).toBeTruthy();
      expect(["run", "recipe"]).toContain(entry.group);
    }
    // The two product runs lead the list; the recipes stay the quieter group.
    expect(demos.filter(entry => entry.group === "run").map(entry => entry.key)).toEqual(["full", "opencsms"]);
    expect(demos.filter(entry => entry.group === "recipe").map(entry => entry.key)).toEqual(["rest-graphql", "rest-database", "workbook"]);
  });

  it("formats the facts the way the run view orders them", () => {
    expect(formatDemoFacts({ tests: 19, failed: 4, partial: 1, cancelled: 0, outcomes: [] })).toBe("19 tests · 4 failed · 1 partial");
    expect(formatDemoFacts({ tests: 1, failed: 0, partial: 0, cancelled: 0, outcomes: [] })).toBe("1 test");
    expect(formatDemoFacts({ tests: 3, failed: 0, partial: 0, cancelled: 1, outcomes: [] })).toBe("3 tests · 1 cancelled");
  });

  it("reads the facts from the trace instead of hardcoding them", async () => {
    const buffer = storedZip({
      "manifest.json": JSON.stringify({ spansEntry: "spans.json", stateEntry: "state.json" }),
      "spans.json": JSON.stringify({
        formatVersion: "2.0",
        resourceSpans: [
          testGroup("t1", "failed"),
          testGroup("t2", "failed"),
          testGroup("t3", "partial"),
          testGroup("t4", "succeeded")
        ]
      }),
      "state.json": JSON.stringify({ formatVersion: "2.0", run: { items: [] }, tests: [] })
    });

    const facts = await summarizeDemo(buffer);
    expect(facts).toEqual({ tests: 4, failed: 2, partial: 1, cancelled: 0, outcomes: ["failed", "failed", "partial", "succeeded"] });
    expect(formatDemoFacts(facts)).toBe("4 tests · 2 failed · 1 partial");
  });
});

function testGroup(testId: string, testOutcome: string) {
  return {
    resource: { attributes: { testId, testName: testId, testOutcome } },
    scopeSpans: []
  };
}
