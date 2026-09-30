import { reactive } from "vue";
import type { TestTrace } from "../trace/model";
import { needsAttention, testGroup, testMatches } from "../trace/format";

export type OutcomeFilter = "all" | "attention" | "passed" | "skipped";
export type TestOrder = "class" | "run";

const orderKey = "prototrace.rail-order";

function readOrder(): TestOrder {
  try { return localStorage.getItem(orderKey) === "run" ? "run" : "class"; } catch { return "class"; }
}

/*
 * The one filter every test list reads: the rail, the run's test list and the step between tests. Filtering
 * in one place filters them all, so walking the failing tests takes one filter, not one per screen.
 */
export const testFilter = reactive({
  query: "",
  outcome: "all" as OutcomeFilter,
  order: readOrder()
});

export function setTestOrder(order: TestOrder) {
  testFilter.order = order;
  try { localStorage.setItem(orderKey, order); } catch { /* storage may be disabled; the order still applies */ }
}

/** A new trace starts unfiltered; the grouping is the reader's habit, so it stays. */
export function resetTestFilter() {
  testFilter.query = "";
  testFilter.outcome = "all";
}

export function matchesOutcome(test: TestTrace, outcome: OutcomeFilter): boolean {
  if (outcome === "attention") return needsAttention(test);
  if (outcome === "passed") return test.outcome === "succeeded";
  if (outcome === "skipped") return test.outcome === "skipped";
  return true;
}

/** How many tests each outcome filter would keep, ignoring the search. */
export function outcomeCounts(tests: TestTrace[]): Record<OutcomeFilter, number> {
  const counts: Record<OutcomeFilter, number> = { all: tests.length, attention: 0, passed: 0, skipped: 0 };
  for (const test of tests) {
    if (needsAttention(test)) counts.attention += 1;
    else if (test.outcome === "succeeded") counts.passed += 1;
    else if (test.outcome === "skipped") counts.skipped += 1;
  }
  return counts;
}

/** The tests the filter keeps, grouped the way the lists show them: by class, or one group in run order. */
export function groupedTests(tests: TestTrace[]): [string, TestTrace[]][] {
  const byGroup = new Map<string, TestTrace[]>();
  for (const test of tests) {
    if (!matchesOutcome(test, testFilter.outcome) || !testMatches(test, testFilter.query)) continue;
    const name = testFilter.order === "run" ? "" : testGroup(test);
    const group = byGroup.get(name);
    if (group) group.push(test); else byGroup.set(name, [test]);
  }
  return [...byGroup.entries()];
}

/** The filtered tests in the order the lists show them, for stepping from one test to the next. */
export function orderedTests(tests: TestTrace[]): TestTrace[] {
  return groupedTests(tests).flatMap(([, group]) => group);
}
