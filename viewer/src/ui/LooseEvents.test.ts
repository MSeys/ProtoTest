import { describe, expect, it } from "vitest";
import { createApp, h, type VNode } from "vue";
import LooseEvents from "./LooseEvents.vue";
import type { Evidence, Moment, TestTrace } from "../trace/model";

function moment(overrides: Partial<Moment>): Moment {
  return {
    at: 5, name: "Server started", kind: "server.started", source: "ProtoTest.AspNetCore",
    outcome: "succeeded", error: null, attributes: {}, sections: [], span: null,
    ...overrides
  };
}

function observation(overrides: Partial<Extract<Evidence, { type: "observation" }>>): Evidence {
  return {
    type: "observation", at: 8, target: "Sheets", kind: "sheets.workbook",
    identifier: "monthly.xlsx", data: null, metadata: {}, span: null,
    ...overrides
  };
}

function testTrace(moments: Moment[], evidence: Evidence[]): TestTrace {
  return {
    number: 1, id: "t1", name: "Suite.Test", className: "Suite", method: "Test", outcome: "succeeded",
    duration: 10, start: 0, end: 10, spans: [], roots: [], byId: new Map(),
    moments, evidence, artifacts: new Map(), items: [], failure: null
  };
}

function mount(view: VNode) {
  const host = document.createElement("div");
  document.body.append(host);
  const app = createApp({ render: () => view });
  app.mount(host);
  return { host, unmount: () => { app.unmount(); host.remove(); } };
}

// Moments and observations with no operation above them would otherwise vanish: the story shows
// operations, the state shows items, and the files show artifacts. This panel is their one home.
describe("LooseEvents", () => {
  it("shows moments and observations in time order with their place on the test clock", () => {
    const { host, unmount } = mount(h(LooseEvents, {
      test: testTrace(
        [moment({ at: 8, name: "Server started" })],
        [observation({ at: 3, kind: "sheets.range", identifier: "Summary!A2:C2", target: "Sheets" })]
      )
    }));

    const rows = [...host.querySelectorAll(".row")];
    expect(rows).toHaveLength(2);
    // The earlier observation reads first even though the moment was listed first.
    expect(rows[0].textContent).toContain("Summary!A2:C2");
    expect(rows[0].textContent).toContain("+3.0 ms");
    expect(rows[1].textContent).toContain("Server started");
    expect(rows[1].textContent).toContain("+8.0 ms");
    unmount();
  });

  it("stays out of the way when every event belongs to an operation", () => {
    const { host, unmount } = mount(h(LooseEvents, { test: testTrace([], []) }));

    expect(host.querySelector(".panel")).toBeNull();
    unmount();
  });

  // Findings already speak in Needs attention; attachments already list under Files.
  it("leaves findings and attachments to the panels that already show them", () => {
    const { host, unmount } = mount(h(LooseEvents, {
      test: testTrace([], [
        { type: "finding", at: 4, message: "Extra fields", status: "Warning", category: null, target: null, tags: [], metadata: {}, span: null },
        { type: "attachment", at: 6, name: "response", artifact: null, span: null }
      ])
    }));

    expect(host.querySelector(".panel")).toBeNull();
    unmount();
  });
});
