import { describe, expect, it } from "vitest";
import { testCodeName, testGroup, testMatches, testTitle } from "./format";
import type { TestTrace } from "./model";

function testTrace(overrides: Partial<TestTrace> = {}): TestTrace {
  return {
    number: 1, id: "t1", name: "Suite.Orders.GetOrder(42)", className: "Suite.Orders", method: "GetOrder",
    outcome: "succeeded", duration: 10, start: 0, end: 10, spans: [], roots: [], byId: new Map(), moments: [],
    evidence: [], artifacts: new Map(), items: [], failure: null,
    ...overrides
  };
}

describe("test names", () => {
  it("keeps the parameter suffix the runner added and drops the class prefix", () => {
    expect(testCodeName(testTrace())).toBe("GetOrder(42)");
    expect(testCodeName(testTrace({ name: "Suite.Orders.GetOrder", method: "GetOrder" }))).toBe("GetOrder");
  });

  it("humanizes the code name and the class", () => {
    expect(testTitle(testTrace({ method: "AFailedOperationRecordsItsDiagnostics", name: "Suite.AFailedOperationRecordsItsDiagnostics" })))
      .toBe("A failed operation records its diagnostics");
    expect(testGroup(testTrace({ className: "ProtoTest.Scale.Journey07" }))).toBe("Journey07");
    expect(testGroup(testTrace({ className: null }))).toBe("Other tests");
  });

  it("matches a query across the name, class and group, case-insensitively, and stays stable when repeated", () => {
    const test = testTrace({ className: "ProtoTest.Scale.Journey07", name: "ProtoTest.Scale.Journey07.RestWritesAreVisibleThroughGraphQL", method: "RestWritesAreVisibleThroughGraphQL" });
    expect(testMatches(test, "graphql")).toBe(true);
    expect(testMatches(test, "journey07")).toBe(true);
    expect(testMatches(test, "rest writes")).toBe(true);
    expect(testMatches(test, "missing")).toBe(false);
    expect(testMatches(test, "  ")).toBe(true);
    // The second call reads the cached values; the answers must not change.
    expect(testTitle(test)).toBe(testTitle(test));
    expect(testMatches(test, "graphql")).toBe(true);
  });
});
