import type {CSSProperties, ReactNode} from 'react';

import styles from './styles.module.css';

/*
 * Test 12's Steps as the viewer draws them: the verdict in the rule's words, the phase band with its
 * untraced second, the framework switch, then the phases. Setup and teardown fold to one line that says what
 * they did; the body is open. Names, durations and summaries are the viewer's for the demo archive.
 */

const total = 1162.6093;
const place = (start: number, length: number): CSSProperties => ({
  left: `${(start / total) * 100}%`,
  width: `max(2px, ${(length / total) * 100}%)`,
});

interface Check {
  label: string;
  passed: boolean;
}

interface Row {
  kind: string;
  /** The execution-vocabulary token the kind is tinted with. */
  tone: string;
  title: string;
  facts?: string;
  app?: boolean;
  duration: string;
  depth: number;
  open?: boolean;
  leaf?: boolean;
  failed?: boolean;
  checks?: Check[];
  start: number;
  length: number;
}

const body: Row[] = [
  {
    kind: 'Data',
    tone: '--type-data',
    title: 'Create · IssueInvoiceRequest',
    duration: '91 ms',
    depth: 0,
    open: true,
    start: 47.245,
    length: 90.971,
  },
  {
    kind: 'Data',
    tone: '--type-data',
    title: 'Build · IssueInvoiceRequest',
    duration: '667 µs',
    depth: 1,
    leaf: true,
    start: 47.3,
    length: 0.667,
  },
  {
    kind: 'Data',
    tone: '--type-data',
    title: 'Provision · IssueInvoiceRequest → InvoiceResponse',
    facts: 'Context set, and 1 more',
    duration: '90 ms',
    depth: 1,
    open: true,
    start: 48.1,
    length: 90,
  },
  {
    kind: 'Northstar',
    tone: '--type-custom',
    title: 'invoice.issue',
    facts: 'Invoice created',
    app: true,
    duration: '6 µs',
    depth: 2,
    leaf: true,
    start: 134.7,
    length: 0.006,
  },
];

const call: Row = {
  kind: 'Call',
  tone: '--type-call',
  title: 'REST · GET /api/v1/organization',
  duration: '7.6 ms',
  depth: 0,
  failed: true,
  start: 1144.738,
  length: 7.552,
  checks: [
    {label: 'status · 200 OK', passed: true},
    {label: 'response shape', passed: false},
  ],
};

function tone(token: string): CSSProperties {
  return {'--node-color': `var(${token})`} as CSSProperties;
}

function Chevron({open}: {open?: boolean}): ReactNode {
  return <i className={`${styles.vChevron} ${open ? styles.vChevronOpen : ''}`} aria-hidden="true" />;
}

function StepRow({row}: {row: Row}): ReactNode {
  return (
    <div className={styles.vNest} style={{'--depth': row.depth} as CSSProperties}>
      <div className={`${styles.vLine} ${row.failed ? styles.vLineFailed : ''}`}>
        {row.leaf ? <span /> : <Chevron open={row.open} />}
        <span className={styles.vPick}>
          <span className={styles.vKind} style={tone(row.tone)}>
            {row.kind}
          </span>
          <span className={styles.vTitle}>{row.title}</span>
          {(row.facts || row.app) && (
            <span className={styles.vFacts}>
              {row.app && <b className={styles.vApp}>app</b>}
              {row.facts}
            </span>
          )}
        </span>
        <span className={styles.vChecks}>
          {row.checks?.map((check) => (
            <span key={check.label} className={check.passed ? styles.vCheck : styles.vCheckFailed}>
              <b aria-hidden="true">{check.passed ? '✓' : '×'}</b>
              {check.label}
            </span>
          ))}
        </span>
        <span className={styles.vBar}>
          <i
            style={{...place(row.start, row.length), background: row.failed ? 'var(--danger)' : undefined}}
          />
        </span>
        <span className={styles.vDuration}>{row.duration}</span>
      </div>
    </div>
  );
}

function PhaseHead({
  name,
  marker,
  summary,
  duration,
  open,
  failed,
}: {
  name: string;
  marker: string;
  summary: string;
  duration: string;
  open?: boolean;
  failed?: boolean;
}): ReactNode {
  return (
    <div className={styles.vPhaseHead} style={{'--marker': `var(${marker})`} as CSSProperties}>
      <Chevron open={open} />
      <i className={styles.marker} aria-hidden="true" />
      <strong>{name}</strong>
      <span className={styles.vSummary}>{summary}</span>
      {failed && <b className={styles.phaseFailed}>Failed</b>}
      <small>{duration}</small>
    </div>
  );
}

export default function StepsView(): ReactNode {
  return (
    <div className={styles.story}>
      <header className={styles.testHead}>
        <span className={styles.testNumber}>12</span>
        <span className={styles.testName}>
          A real wait does not close the due window
          <small>Failure drills · ARealWaitDoesNotCloseTheDueWindow</small>
        </span>
        <span className={styles.outcome}>
          <i className={styles.outcomeDot} />
          Failed
          <em>1.16 s</em>
        </span>
      </header>

      <section className={styles.vVerdict} aria-label="Why this test did not pass">
        <span className={styles.vRule}>Assertion</span>
        <strong>Assert response shape</strong>
        <span className={styles.on}>on REST · GET /api/v1/organization</span>
        <p>
          $.status: expected &quot;past_due&quot;, got <b>&quot;active&quot;</b>
        </p>
      </section>

      <div className={styles.vBand} aria-hidden="true">
        <i style={{...place(0, 46.325), background: 'var(--phase-setup)'}} />
        <i style={{...place(46.349, 1108.669), background: 'var(--phase-execution)'}} />
        <i className={styles.gapBar} style={place(138.216, 1006.522)} />
        <i style={{...place(1155.097, 7.396), background: 'var(--phase-teardown)'}} />
      </div>
      <p className={styles.vLegend}>
        <span style={{'--marker': 'var(--phase-setup)'} as CSSProperties}>
          Setup <b>46 ms</b>
        </span>
        <span style={{'--marker': 'var(--phase-execution)'} as CSSProperties}>
          Execution <b>1.11 s</b>, 1.01 s without an operation
        </span>
        <span style={{'--marker': 'var(--phase-teardown)'} as CSSProperties}>
          Teardown <b>7.4 ms</b>
        </span>
      </p>

      <div className={styles.vToolbar}>
        <span>Framework</span>
        <span className={styles.vSegment}>Show</span>
        <span className={`${styles.vSegment} ${styles.vSegmentOn}`}>Dim</span>
        <span className={styles.vSegment}>Hide</span>
      </div>

      <div className={styles.vPhases}>
        <section className={styles.vPhase}>
          <PhaseHead
            name="Setup"
            marker="--phase-setup"
            summary="20 operations, 6 clients initialized, Create · ProvisionTenantRequest (45 ms)"
            duration="46 ms"
          />
        </section>
        <section className={styles.vPhase}>
          <PhaseHead
            name="Execution"
            marker="--phase-execution"
            summary="9 operations, Create · IssueInvoiceRequest (91 ms), REST · GET /api/v1/organization (7.6 ms)"
            duration="1.11 s"
            open
            failed
          />
          <div className={styles.vRows}>
            {body.map((row) => (
              <StepRow key={row.title} row={row} />
            ))}
            <div className={`${styles.vLine} ${styles.vGap}`}>
              <i className={styles.vGapMark} aria-hidden="true" />
              <span className={styles.vGapText}>
                <strong>1.01 s with no recorded operation</strong>
                <small>
                  Until REST · GET /api/v1/organization started, +1.15 s into the test. A wait, or work the
                  trace could not see.
                </small>
              </span>
              <span className={styles.vChecks} />
              <span className={styles.vBar}>
                <i className={styles.gapBar} style={place(138.216, 1006.522)} />
              </span>
              <span className={styles.vDuration}>1.01 s</span>
            </div>
            <StepRow row={call} />
            <p className={styles.vObserved}>
              Observed http.response · GET /api/v1/organization <span>on Northstar·Northstar</span>
            </p>
          </div>
        </section>
        <section className={styles.vPhase}>
          <PhaseHead
            name="Teardown"
            marker="--phase-teardown"
            summary="21 operations, 4 resources released, 3 files published, Cleanup · TenantResponse (3.1 ms)"
            duration="7.4 ms"
          />
        </section>
      </div>
    </div>
  );
}
