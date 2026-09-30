import {useId, type CSSProperties, type ReactNode} from 'react';

import styles from './styles.module.css';

/*
 * The viewer's inspector for the failing check: what ran, when, the exception it threw, and the source
 * line the trace recorded around it. The location and code are the inspector's own: FailureDrills.cs
 * line 36, with the assertion highlighted.
 */

const source = {
  file: 'FailureDrills.cs',
  line: 36,
  method: 'Northstar.ProtoTest.FailureDrills.ARealWaitDoesNotCloseTheDueWindow',
  lines: [
    [32, ''],
    [33, 'await Task.Delay(TimeSpan.FromSeconds(1));'],
    [34, ''],
    [35, 'using var organization = await Proto.Context.Rest().GetAsync("/api/v1/organization");'],
    [36, 'organization'],
    [37, '    .Should.HaveHttpStatus(HttpStatusCode.OK)'],
    [38, '    .Should.MatchShape(new { status = SubscriptionStatuses.PastDue });'],
    [39, '}'],
    [40, ''],
  ] as const,
};

function tone(token: string): CSSProperties {
  return {'--node-color': `var(${token})`} as CSSProperties;
}

export default function CheckView(): ReactNode {
  const id = useId();
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
        <span className={styles.meta}>2.2 ms · +1.15 s into the test</span>
        <span className={styles.kind} style={tone('--phase-execution')}>
          execution
        </span>
      </p>
      <p className={styles.checkOp}>assert.json.shape from ProtoTest.Rest</p>

      <nav className={styles.detailsIndex} aria-label="Parts of this operation">
        <a href={`#${id}-error`} onClick={() => { const error = document.getElementById(`${id}-error`); if (error instanceof HTMLDetailsElement) error.open = true; }}>Error</a><a href={`#${id}-source`}>Source</a><a href={`#${id}-comparison`}>Comparison</a>
      </nav>
      <details className={styles.exception} id={`${id}-error`}>
        <summary>Exception message</summary>
        <b>JsonShapeMismatchException</b>
        <span className={styles.exceptionNote}>Shape mismatch failed with 1 error(s):</span>
        <span className={styles.exceptionNote}>
          • [$.status]: Values did not match. (Expected: &quot;past_due&quot;, Actual: &quot;active&quot;)
        </span>
      </details>

      <div className={styles.source} id={`${id}-source`}>
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
      <section className={styles.shape} id={`${id}-comparison`}>
        <header className={styles.shapeHead}>Validated document</header>
        <div className={styles.mismatch}><span className={styles.property}>"status"</span>: <s>"past_due"</s> <strong>"active"</strong></div>
      </section>
    </div>
  );
}
