import type {CSSProperties, ReactNode} from 'react';

import styles from './styles.module.css';

/*
 * Steps for test 12: the verdict, folded lifecycle and the gap before its failing call.
 */

interface Check {
  label: string;
  passed: boolean;
}

interface Row {
  chip: string;
  /** The execution-vocabulary token the chip is tinted with. */
  tone: string;
  title: string;
  facts?: string;
  duration: string;
  checks?: Check[];
  /** A folded run of framework steps: drawn with the expand box and a quiet title. */
  folded?: boolean;
  depth?: number;
  failed?: boolean;
  start?: number;
  length?: number;
}

interface Phase {
  name: string;
  marker: string;
  duration: string;
  failed?: boolean;
  rows: Row[];
}

const mismatches = [
  {property: 'status', expected: '"past_due"', actual: '"active"'},
];

const phases: Phase[] = [
  {
    name: 'Setup',
    marker: '--phase-setup',
    duration: '46 ms',
    rows: [
      {chip: 'Setup', tone: '--phase-setup', title: 'Setup', facts: 'Clients, tenant provisioning and authentication', duration: '46 ms', folded: true, start: 0, length: 46.325},
    ],
  },
  {
    name: 'Execution',
    marker: '--phase-execution',
    duration: '1.11 s',
    failed: true,
    rows: [
      {chip: 'Data', tone: '--type-data', title: 'Create · IssueInvoiceRequest', duration: '91 ms', start: 47.245, length: 90.971},
      {chip: 'Gap', tone: '--muted', title: '1.01 s with no recorded operation', facts: 'Execution', duration: '1.01 s', start: 138.216, length: 1006.522},
      {
        chip: 'Call',
        tone: '--type-call',
        title: 'REST · GET /api/v1/organization',
        duration: '7.6 ms',
        failed: true,
        start: 1144.738,
        length: 7.552,
        checks: [
          {label: 'status · 200 OK', passed: true},
          {label: 'response shape', passed: false},
        ],
      },
    ],
  },
  {
    name: 'Teardown',
    marker: '--phase-teardown',
    duration: '7.4 ms',
    rows: [
      {chip: 'Teardown', tone: '--phase-teardown', title: 'Teardown', facts: 'Release owned resources and dispose the context', duration: '7.4 ms', folded: true, start: 1155.097, length: 7.396},
    ],
  },
];

function tone(token: string): CSSProperties {
  return {'--node-color': `var(${token})`} as CSSProperties;
}

function StepRow({row}: {row: Row}): ReactNode {
  return (
    <div className={`${styles.row} ${row.failed ? styles.failed : ''}`} style={{'--depth': row.depth ?? 0} as CSSProperties}>
      {row.folded ? <i className={styles.fold} aria-label="folded" /> : <i className={styles.node} style={tone(row.tone)} />}
      <span className={styles.kind} style={tone(row.tone)}>
        {row.chip}
      </span>
      <span className={`${styles.label} ${row.folded ? styles.quiet : ''} ${row.facts ? '' : styles.wide}`}>{row.title}</span>
      {row.facts && <span className={styles.facts}>{row.facts}</span>}
      <span className={styles.duration}>{row.duration}</span>
      <span className={styles.stepTrack}><i className={row.chip === 'Gap' ? styles.gapBar : undefined} style={{left: `${(row.start ?? 0) / 1162.6093 * 100}%`, width: `${(row.length ?? 0) / 1162.6093 * 100}%`, ...(row.chip === 'Gap' ? {} : {background: `var(${row.tone})`})}} /></span>
      {row.checks && (
        <span className={styles.checks}>
          {row.checks.map((check) => (
            <span key={check.label} className={`${styles.check} ${check.passed ? '' : styles.checkFailed}`}>
              <b aria-hidden="true">{check.passed ? '✓' : '×'}</b>
              {check.label}
            </span>
          ))}
        </span>
      )}
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

      <div className={styles.card}>
        <div className={styles.cardHead}>
          <span className={styles.kind} style={tone('--type-assertion')}>
            Assertion
          </span>
          <strong>Assert response shape</strong>
          <span className={styles.verdict}>Execution, +1.15 s</span>
          <span className={styles.on}>on REST · GET /api/v1/organization</span>
        </div>
        <div className={styles.verdictDetail}>
          {mismatches.map((mismatch) => (
            <div key={mismatch.property} className={styles.mismatch}>
              <span>$.{mismatch.property}: expected {mismatch.expected}, got <strong>{mismatch.actual}</strong></span>
            </div>
          ))}
        </div>
      </div>

      <div className={styles.rows}>
        {phases.map((phase) => (
          <section key={phase.name} className={styles.phase}>
            <header className={styles.phaseHead} style={{'--marker': `var(${phase.marker})`} as CSSProperties}>
              <i className={styles.marker} />
              <span>{phase.name}</span>
              {phase.failed && <b className={styles.phaseFailed}>Failed</b>}
              <small>{phase.duration}</small>
            </header>
            {phase.rows.map((row) => (
              <StepRow key={`${phase.name}-${row.title}`} row={row} />
            ))}
          </section>
        ))}
      </div>
    </div>
  );
}
