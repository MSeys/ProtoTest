import type {CSSProperties, ReactNode} from 'react';

import Frame from '@site/src/components/Frame';
import styles from './styles.module.css';

/*
 * One test from the bundled demo trace, TheOrganizationReportsItsPlanAndProjectCount, drawn the way ProtoTrace
 * draws it: the failing check first, as the document it validated, then the story - framework steps folded,
 * each call carrying its checks. Names, values and durations are copied from that trace, not invented.
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

/* Where the check sits in the suite: the location the trace recorded, and the lines it embedded around it. */
const source = {
  file: 'DiagnosticsShowcase.cs',
  line: 119,
  method: 'DiagnosticsShowcase.TheOrganizationReportsItsPlanAndProjectCount',
  lines: [
    [116, 'using var organization = await Proto.Context.Rest().GetAsync("/api/v1/organization");'],
    [117, ''],
    [118, '// Assert'],
    [119, 'organization.Should.HaveHttpStatus(HttpStatusCode.OK).ShouldMatchShape(new'],
    [120, '{'],
    [121, '    projectCount = 99,'],
    [122, '    planId = "nonexistent-plan"'],
  ] as const,
};

const phases: Phase[] = [
  {
    name: 'Setup',
    marker: '--phase-setup',
    duration: '24 ms',
    rows: [
      {chip: 'Framework', tone: '--type-extension', title: '5 extensions', facts: '4 clients initialized', duration: '253 µs', folded: true},
      {chip: 'Extension', tone: '--type-extension', title: 'Before · NorthstarTenantAttribute', facts: 'context set', duration: '23 ms'},
      {chip: 'Call', tone: '--type-call', title: 'REST · POST /test-support/tenants', duration: '23 ms', depth: 1, checks: [{label: 'status · 201 Created', passed: true}]},
    ],
  },
  {
    name: 'Execution',
    marker: '--phase-execution',
    duration: '15 ms',
    failed: true,
    rows: [
      {
        chip: 'Call',
        tone: '--type-call',
        title: 'REST · GET /api/v1/organization',
        duration: '12 ms',
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
    duration: '14 ms',
    rows: [
      {chip: 'Extension', tone: '--type-extension', title: 'After · NorthstarTenantAttribute', duration: '13 ms'},
      {chip: 'Call', tone: '--type-call', title: 'REST · DELETE /test-support/tenants/{tenant}', duration: '13 ms', depth: 1, checks: [{label: 'status · 204 NoContent', passed: true}]},
      {chip: 'Framework', tone: '--type-extension', title: '7 framework steps', facts: '6 state changes', duration: '528 µs', folded: true},
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

export default function TraceView(): ReactNode {
  return (
    <Frame
      head={
        <>
          <span className={styles.testName}>
            The organization reports its plan and project count
            <small>TheOrganizationReportsItsPlanAndProjectCount</small>
          </span>
          <span className={styles.outcome}>
            <i className={styles.outcomeDot} />
            Failed
            <em>53 ms</em>
          </span>
        </>
      }
      foot={
        <>
          From <span className={styles.archive}>prototest-demo.prototrace</span>, as the viewer shows it.
        </>
      }>
      <div className={styles.card}>
        <div className={styles.cardHead}>
          <span className={styles.kind} style={tone('--type-assertion')}>
            Check
          </span>
          <strong>Assert response shape</strong>
          <span className={styles.verdict}>2 mismatches</span>
          <span className={styles.on}>on REST · GET /api/v1/organization</span>
        </div>
        <div className={styles.source}>
          <div className={styles.sourceHead}>
            <b>
              {source.file}:{source.line}
            </b>
            <small>{source.method}</small>
          </div>
          <pre>
            {source.lines.map(([number, text]) => (
              <span key={number} className={number === source.line ? styles.current : undefined}>
                <b>{number}</b>
                {text || ' '}
              </span>
            ))}
          </pre>
        </div>
        <div className={styles.shape}>
          <div className={styles.shapeHead}>Validated document</div>
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
    </Frame>
  );
}
