import type { Evidence, Moment, TestTrace } from "../trace/model";

export type LooseObservation = Extract<Evidence, { type: "observation" }>;

export type LooseEvent =
  | { type: "moment"; at: number; moment: Moment }
  | { type: "observation"; at: number; observation: LooseObservation };

/*
 * Moments and observations the trace recorded with no operation above them, in the order they happened.
 * Findings already speak in Needs attention; attachments already list under Files.
 */
export function looseEvents(test: TestTrace): LooseEvent[] {
  return [
    ...test.moments.map((moment): LooseEvent => ({ type: "moment", at: moment.at, moment })),
    ...test.evidence
      .filter((entry): entry is LooseObservation => entry.type === "observation")
      .map((observation): LooseEvent => ({ type: "observation", at: observation.at, observation }))
  ].sort((left, right) => left.at - right.at);
}
