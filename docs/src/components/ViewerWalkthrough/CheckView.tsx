import type {CSSProperties, ReactNode} from 'react';

import styles from './styles.module.css';

/*
 * The viewer's inspector for the failing check: what ran, when, the exception it threw, and the source
 * line the trace recorded around it. The location and code are the inspector's own: DiagnosticsShowcase.cs
 * line 121, with the assertion highlighted.
 */

const source = {
  file: 'DiagnosticsShowcase.cs',
  line: 121,
  method: 'ProtoTest.Demo.DiagnosticsShowcase.TheOrganizationReportsItsPlanAndProjectCount',
  lines: [
    [117, '// Act'],
    [118, 'using var organization = await Proto.Context.Rest().GetAsync("/api/v1/organization");'],
    [119, ''],
    [120, '// Assert'],
    [121, 'organization.Should.HaveHttpStatus(HttpStatusCode.OK).Should.MatchShape(new'],
    [122, '{'],
    [123, '    projectCount = 99,'],
  ] as const,
};

function tone(token: string): CSSProperties {
  return {'--node': `var(${token})`} as CSSProperties;
}

export default function CheckView(): ReactNode {
  return (
    <div className={styles.checkView}>
      <p className={styles.crumb}>
        Test execution / <span className={styles.crumbOp}>REST · GET /api/v1/organization</span>
      </p>

      <header className={styles.checkHead}>
        <span className={styles.kind} style={tone('--type-assertion')}>
          Check
        </span>
        <strong>Assert response shape</strong>
      </header>

      <p className={styles.checkStatus}>
        <span className={`${styles.checkStatusOutcome} ${styles.failedText}`}>
          <i className={styles.outcomeDot} />
          Failed
        </span>
        <span className={styles.meta}>2.0 ms · +31 ms into the test</span>
        <span className={styles.kind} style={tone('--phase-execution')}>
          Execution
        </span>
      </p>
      <p className={styles.checkOp}>assert.json.shape from ProtoTest.Rest</p>

      <div className={styles.exception}>
        <b>JsonShapeMismatchException</b>
        <span className={styles.exceptionNote}>
          Response shape mismatch · 2 properties · projectCount, planId
        </span>
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
    </div>
  );
}
