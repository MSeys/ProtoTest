import { afterEach, describe, expect, it } from "vitest";
import { orderedTests, outcomeCounts, resetTestFilter, setTestOrder, testFilter } from "./testFilter";
import type { TestTrace } from "../trace/model";

function testTrace(number: number, outcome: TestTrace["outcome"], className = "Suite"): TestTrace {
  return {
    number, id: `t${number}`, name: `${className}.Test${number}`, className, method: `Test${number}`,
    outcome, duration: 10, start: number, end: number + 10, spans: [], roots: [], byId: new Map(),
    moments: [], evidence: [], artifacts: new Map(), items: [], failure: null
  };
}

afterEach(() => {
  resetTestFilter();
  setTestOrder("class");
});

describe("testFilter", () => {
  // Needs attention is everything short of pass and skip, so a cancelled or unknown test is never lost.
  it("counts each outcome filter over the whole run", () => {
    const tests = [testTrace(1, "succeeded"), testTrace(2, "failed"), testTrace(3, "cancelled"), testTrace(4, "unknown"), testTrace(5, "skipped")];

    expect(outcomeCounts(tests)).toEqual({ all: 5, attention: 3, passed: 1, skipped: 1 });
  });

  // The step between tests walks the list the reader sees: its grouping, its filter and its search.
  it("orders the filtered tests the way the list shows them", () => {
    const tests = [testTrace(1, "failed", "Beta"), testTrace(2, "succeeded", "Alpha"), testTrace(3, "failed", "Beta")];

    expect(orderedTests(tests).map(test => test.number)).toEqual([1, 3, 2]);
    setTestOrder("run");
    expect(orderedTests(tests).map(test => test.number)).toEqual([1, 2, 3]);
    testFilter.outcome = "attention";
    expect(orderedTests(tests).map(test => test.number)).toEqual([1, 3]);
    testFilter.query = "test3";
    expect(orderedTests(tests).map(test => test.number)).toEqual([3]);
  });
});
