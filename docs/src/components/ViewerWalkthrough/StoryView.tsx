import type {CSSProperties, ReactNode} from 'react';

import styles from './styles.module.css';

/*
 * The viewer's Story view for the failed test: the failure card first - the check and the document it
 * validated - then the lifecycle, framework steps folded, each call carrying its checks. The failure
 * card shows what the viewer's story shows; the recorded source line moves to the Check view, where the
 * viewer's inspector puts it.
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
}

interface Phase {
  name: string;
  marker: string;
  duration: string;
  failed?: boolean;
  rows: Row[];
}

const mismatches = [
  {property: 'projectCount', expected: '99', actual: '0'},
  {property: 'planId', expected: '"nonexistent-plan"', actual: '"free"'},
];

const phases: Phase[] = [
  {
    name: 'Setup',
    marker: '--phase-setup',
    duration: '15 ms',
    rows: [
      {chip: 'Framework', tone: '--type-extension', title: '2 extensions', facts: 'SqlConnectionHook, NorthstarTenantAttribute', duration: '644 µs', folded: true},
      {chip: 'Extension', tone: '--type-extension', title: 'Before · NorthstarTenantAttribute', facts: 'context set', duration: '13 ms'},
      {chip: 'Data', tone: '--type-data', title: 'Provision · ProvisionTenantRequest → TenantResponse', duration: '13 ms', depth: 1},
    ],
  },
  {
    name: 'Execution',
    marker: '--phase-execution',
    duration: '20 ms',
    failed: true,
    rows: [
      {
        chip: 'Call',
        tone: '--type-call',
        title: 'REST · GET /api/v1/organization',
        duration: '15 ms',
        failed: true,
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
    duration: '11 ms',
    rows: [
      {chip: 'Context', tone: '--type-context', title: 'Dispose execution context', duration: '7.5 ms'},
      {chip: 'Data', tone: '--type-data', title: 'Cleanup · TenantResponse', duration: '6.8 ms', depth: 1},
      {chip: 'Framework', tone: '--type-extension', title: '3 framework steps', duration: '25 µs', folded: true},
    ],
  },
];

function tone(token: string): CSSProperties {
  return {'--node': `var(${token})`} as CSSProperties;
}

function StoryRow({row}: {row: Row}): ReactNode {
  return (
    <div className={`${styles.row} ${row.failed ? styles.failed : ''}`} style={{'--depth': row.depth ?? 0} as CSSProperties}>
      {row.folded ? <i className={styles.fold} aria-label="folded" /> : <i className={styles.node} style={tone(row.tone)} />}
      <span className={styles.kind} style={tone(row.tone)}>
        {row.chip}
      </span>
      <span className={`${styles.label} ${row.folded ? styles.quiet : ''} ${row.facts ? '' : styles.wide}`}>{row.title}</span>
      {row.facts && <span className={styles.facts}>{row.facts}</span>}
      <span className={styles.duration}>{row.duration}</span>
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

export default function StoryView(): ReactNode {
  return (
    <div className={styles.story}>
      <header className={styles.testHead}>
        <span className={styles.testNumber}>36</span>
        <span className={styles.testName}>
          The organization reports its plan and project count
          <small>Diagnostics showcase · TheOrganizationReportsItsPlanAndProjectCount</small>
        </span>
        <span className={styles.outcome}>
          <i className={styles.outcomeDot} />
          Failed
          <em>47 ms</em>
        </span>
      </header>

      <div className={styles.card}>
        <div className={styles.cardHead}>
          <span className={styles.kind} style={tone('--type-assertion')}>
            Check
          </span>
          <strong>Assert response shape</strong>
          <span className={styles.verdict}>2 mismatches</span>
          <span className={styles.on}>on REST · GET /api/v1/organization</span>
        </div>
        <div className={styles.shape}>
          <div className={styles.shapeHead}>
            Validated document
            <small>
              <b className={styles.matched}>✓ matched</b> <b className={styles.differs}>× expected, then actual</b>
            </small>
          </div>
          {mismatches.map((mismatch) => (
            <div key={mismatch.property} className={styles.mismatch}>
              <b aria-label="mismatch">×</b>
              <span className={styles.property}>"{mismatch.property}"</span>
              <span className={styles.colon}>:</span>
              <s>{mismatch.expected}</s>
              <i aria-hidden="true">→</i>
              <strong>{mismatch.actual}</strong>
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
              <StoryRow key={`${phase.name}-${row.title}`} row={row} />
            ))}
          </section>
        ))}
      </div>
    </div>
  );
}
