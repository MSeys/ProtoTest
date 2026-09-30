import { afterEach, describe, expect, it, vi } from "vitest";
import { createApp, h, nextTick, type VNode } from "vue";
import TestRail from "./TestRail.vue";
import { resetTestFilter, setTestOrder, testFilter } from "./testFilter";
import type { Outcome, Span, TestTrace } from "../trace/model";

function span(overrides: Partial<Span>): Span {
  return {
    id: "span", parent: null, children: [], depth: 0, name: "Assert status", kind: "assert.http.status",
    source: "ProtoTest.Rest", phase: "execution", status: "failed", error: null, start: 0, duration: 1, end: 1,
    count: 1, attributes: {}, sections: [], moments: [], evidence: [], itemKey: null, item: null, changes: [],
    test: null, ...overrides
  };
}

function testTrace(number: number, outcome: TestTrace["outcome"], failure: TestTrace["failure"] = null, className = "Suite"): TestTrace {
  return {
    number, id: `t${number}`, name: `${className}.Test${number}`, className, method: `Test${number}`,
    outcome, duration: 10, start: number * 10, end: number * 10 + 10, spans: [], roots: [], byId: new Map(),
    moments: [], evidence: [], artifacts: new Map(), items: [], failure
  };
}

function counts(tests: TestTrace[]): Record<Outcome, number> {
  const result: Record<Outcome, number> = { succeeded: 0, partial: 0, failed: 0, cancelled: 0, skipped: 0, unknown: 0 };
  tests.forEach(test => { result[test.outcome] += 1; });
  return result;
}

function rail(tests: TestTrace[], overrides: Record<string, unknown> = {}) {
  return h(TestRail, {
    tests, counts: counts(tests), atRun: false, hrefFor: (test: TestTrace) => `#/test/${test.id}/story`,
    onSelect: () => {}, ...overrides
  });
}

function mount(view: VNode) {
  const host = document.createElement("div");
  document.body.append(host);
  const app = createApp({ render: () => view });
  app.mount(host);
  return { host, unmount: () => { app.unmount(); host.remove(); } };
}

afterEach(() => {
  resetTestFilter();
  setTestOrder("class");
});

// A test that did not pass says why in the list. Without a failing operation the outcome is still
// stated in words: never colour alone.
describe("TestRail reasons", () => {
  it("names the outcome when no failing operation recorded one", () => {
    const { host, unmount } = mount(rail([testTrace(1, "succeeded"), testTrace(2, "cancelled")]));

    const reasons = [...host.querySelectorAll(".rail-row .reason")].map(entry => entry.textContent?.trim());
    expect(reasons).toEqual(["Cancelled"]);
    unmount();
  });

  it("names the failing check when the trace recorded one", () => {
    const failed = span({});
    const { host, unmount } = mount(rail([testTrace(1, "failed", { span: failed, check: null, mismatches: [], call: null })]));

    expect(host.querySelector(".rail-row .reason")?.textContent?.trim()).toBe("Assert status");
    unmount();
  });

  // A filter that matches nothing leaves the reader somewhere; the list offers the way back.
  it("offers a way back from a search that matches nothing", async () => {
    const { host, unmount } = mount(rail([testTrace(1, "succeeded")]));

    const input = host.querySelector<HTMLInputElement>("input[type='search']")!;
    input.value = "zzz";
    input.dispatchEvent(new Event("input", { bubbles: true }));
    await nextTick();
    expect(host.querySelector(".rail-row")).toBeNull();

    const reset = [...host.querySelectorAll("button")]
      .find(entry => entry.textContent?.trim() === "Show all tests");
    expect(reset).toBeTruthy();
    reset!.click();
    await nextTick();
    expect(host.querySelector(".rail-row")).toBeTruthy();
    unmount();
  });

  // The status dot is silent, so the open test is marked and every row states its outcome in words.
  it("marks the selected row as the current page and names each outcome", () => {
    const tests = [testTrace(1, "succeeded"), testTrace(2, "failed")];
    const { host, unmount } = mount(rail(tests, { selected: tests[1] }));

    const rows = [...host.querySelectorAll(".rail-row")];
    expect(rows.map(row => row.getAttribute("aria-current"))).toEqual([null, "page"]);
    expect(rows[0].textContent).toContain("Succeeded");
    expect(rows[1].textContent).toContain("Failed");
    unmount();
  });
});

// The run is the list's first entry, so the run and its tests are reached from one place on every screen.
describe("TestRail run row", () => {
  it("leads with the run and its verdict, current on the run screen", () => {
    const tests = [testTrace(1, "succeeded"), testTrace(2, "failed")];
    const { host, unmount } = mount(rail(tests, { atRun: true }));

    const row = host.querySelector<HTMLAnchorElement>(".run-row");
    expect(row?.getAttribute("href")).toBe("#/");
    expect(row?.getAttribute("aria-current")).toBe("page");
    expect(row?.textContent?.replace(/\s+/g, " ").trim()).toBe("Run, 1 failed, 1 passed");
    unmount();
  });

  it("opens the run in place on a plain click", () => {
    const run = vi.fn();
    const { host, unmount } = mount(rail([testTrace(1, "succeeded")], { onRun: run }));

    host.querySelector<HTMLAnchorElement>(".run-row")!.dispatchEvent(new MouseEvent("click", { bubbles: true, cancelable: true, button: 0 }));
    expect(run).toHaveBeenCalledTimes(1);
    unmount();
  });
});

// Every test is a link, so a test opens in a new tab like any page; a plain click opens it in place.
describe("TestRail links", () => {
  it("links each test and leaves a modified click to the browser", () => {
    const select = vi.fn();
    const tests = [testTrace(1, "succeeded")];
    const { host, unmount } = mount(rail(tests, { onSelect: select }));

    const row = host.querySelector<HTMLAnchorElement>(".rail-row")!;
    expect(row.getAttribute("href")).toBe("#/test/t1/story");
    const modified = new MouseEvent("click", { bubbles: true, cancelable: true, button: 0, ctrlKey: true });
    row.dispatchEvent(modified);
    expect(select).not.toHaveBeenCalled();
    expect(modified.defaultPrevented).toBe(false);

    const plain = new MouseEvent("click", { bubbles: true, cancelable: true, button: 0 });
    row.dispatchEvent(plain);
    expect(select).toHaveBeenCalledWith(tests[0]);
    expect(plain.defaultPrevented).toBe(true);
    unmount();
  });
});

// One filter for every list: what the rail filters, the run list and the step between tests follow.
describe("TestRail shared filter", () => {
  it("filters through the shared state and offers only outcomes the run had", async () => {
    const tests = [testTrace(1, "succeeded"), testTrace(2, "failed"), testTrace(3, "partial")];
    const { host, unmount } = mount(rail(tests));

    const chips = [...host.querySelectorAll<HTMLButtonElement>("button.chip")].map(chip => chip.textContent?.replace(/\s+/g, " ").trim());
    expect(chips).toEqual(["All3", "Needs attention2", "Passed1"]);

    [...host.querySelectorAll<HTMLButtonElement>("button.chip")][1].click();
    await nextTick();
    expect(testFilter.outcome).toBe("attention");
    expect([...host.querySelectorAll(".rail-row b")].map(entry => entry.textContent)).toEqual(["02", "03"]);
    unmount();
  });

  it("groups by class or keeps the run order", async () => {
    const tests = [testTrace(1, "succeeded", null, "Beta"), testTrace(2, "succeeded", null, "Alpha"), testTrace(3, "succeeded", null, "Beta")];
    const { host, unmount } = mount(rail(tests));

    expect([...host.querySelectorAll(".rail-list h3")].map(entry => entry.textContent)).toEqual(["Beta", "Alpha"]);
    expect([...host.querySelectorAll(".rail-row b")].map(entry => entry.textContent)).toEqual(["01", "03", "02"]);

    [...host.querySelectorAll<HTMLButtonElement>(".order button")].find(entry => entry.textContent === "In run order")!.click();
    await nextTick();
    expect(host.querySelectorAll(".rail-list h3")).toHaveLength(0);
    expect([...host.querySelectorAll(".rail-row b")].map(entry => entry.textContent)).toEqual(["01", "02", "03"]);
    unmount();
  });
});

// Beside open details the list steps aside to its numbers: each still names its test and still opens it.
describe("TestRail slim", () => {
  it("keeps the numbers and outcomes, names each test on hover, and offers the whole list back", () => {
    const expand = vi.fn();
    const tests = [testTrace(1, "succeeded"), testTrace(2, "failed")];
    const { host, unmount } = mount(rail(tests, { slim: true, onExpand: expand }));

    expect(host.querySelector(".rail-head")).toBeNull();
    expect(host.querySelector(".rail-row .name")).toBeNull();
    expect([...host.querySelectorAll(".rail-row")].map(row => row.getAttribute("title"))).toEqual(["01 Test1", "02 Test2"]);
    host.querySelector<HTMLButtonElement>("button.expand")!.click();
    expect(expand).toHaveBeenCalledTimes(1);
    unmount();
  });
});
