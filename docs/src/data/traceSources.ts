/*
 * The runs the Learn components read.
 *
 * The values in the components and in data/failureDrills.ts were read back from a recording of
 * samples/Northstar.ProtoTest with `ProtoTest__Sample__Drills=true`, so the four deliberate failures
 * sit in the archive next to the tests that fix them. The lesson trace generator
 * (eng/generate-lesson-traces.ps1) will write the lesson archives to docs/static/lessons/ and host
 * them; when it lands, set `file` and `href` below and every component that takes a source picks the
 * switch up. Nothing else needs an edit.
 */

export interface TraceSource {
  /** What the archive holds, in the reader's words. */
  what: string;
  /** The archive's path in the repository, once the generator writes it. */
  file?: string;
  /** A link that opens the same run in the viewer. */
  href?: string;
}

/** The drill run: the four failures and the tests that fix them. */
export const drillRun: TraceSource = {
  what: 'a run of samples/Northstar.ProtoTest with the drills enabled',
};
