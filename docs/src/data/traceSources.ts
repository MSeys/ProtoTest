/*
 * The archives the Learn components read.
 *
 * eng/generate-lesson-traces.ps1 runs one filtered test per file against samples/Northstar.ProtoTest
 * and writes the result to docs/static/lessons/, so every entry below names a file the site serves and
 * a reader can download. The values in data/failureDrills.ts are read from those committed archives:
 * names, statuses and messages come from the run, and the durations are the recording's own, so a
 * rerun of the drill may differ in a duration and never in a name or a status.
 */

export interface TraceSource {
  /** What the archive holds, in the reader's words. */
  what: string;
  /** The archive's path in the repository. */
  file?: string;
  /** The site path that serves the archive, so a reader can download it. */
  href?: string;
}

function archive(fileName: string, what: string): TraceSource {
  return {
    what,
    file: `docs/static/lessons/${fileName}`,
    // The pathname protocol keeps Docusaurus from treating a static file as a route.
    href: `pathname:///lessons/${fileName}`,
  };
}

/** One archive per test the lessons read, as the lesson trace generator writes them. */
export const lessonTraces = {
  timeDrill: archive('l0-time-drill.prototrace', 'the time drill, run with the drills enabled'),
  timeFix: archive('l0-time-fix.prototrace', 'the test that moves the clock instead of waiting'),
  stateDrill: archive('l0-state-drill.prototrace', 'the state drill, run with the drills enabled'),
  stateFix: archive('l0-state-fix.prototrace', 'the test that reads only the data it created'),
  environmentDrill: archive('l0-environment-drill.prototrace', 'the environment drill, run with the drills enabled'),
  environmentFix: archive('l0-environment-fix.prototrace', 'the test that takes the address from the composition'),
  visibilityDrill: archive('l0-visibility-drill.prototrace', 'the visibility drill, run with the drills enabled'),
  visibilityFix: archive('l0-visibility-fix.prototrace', 'the test that asserts the body the application sent'),
  firstJourney: archive('l1-first-journey.prototrace', 'the project journey the first lesson writes'),
  brokerSkip: archive('l2-broker-skip.prototrace', 'the broker journey, skipped because no broker is configured'),
  clockWindow: archive('l3-clock-window.prototrace', 'the billing period closed on the test clock'),
  coverage: archive('l4-coverage.prototrace', 'one REST write read back over GraphQL'),
  artifacts: archive('l4-artifacts.prototrace', 'a downloaded workbook and the model assertions'),
};

export type DrillPairId = 'time' | 'state' | 'environment' | 'visibility';

/** The drill and the test that does the same journey the right way, per question. */
export const drillPairs: Record<DrillPairId, {drill: TraceSource; fix: TraceSource}> = {
  time: {drill: lessonTraces.timeDrill, fix: lessonTraces.timeFix},
  state: {drill: lessonTraces.stateDrill, fix: lessonTraces.stateFix},
  environment: {drill: lessonTraces.environmentDrill, fix: lessonTraces.environmentFix},
  visibility: {drill: lessonTraces.visibilityDrill, fix: lessonTraces.visibilityFix},
};

/** The provenance line the aggregate components print for their recorded values. */
export const drillRun: TraceSource = {
  what: 'a recording of samples/Northstar.ProtoTest with the drills enabled',
};
