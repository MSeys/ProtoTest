import {useId, type CSSProperties, type ReactNode} from 'react';

import styles from './styles.module.css';

/*
 * The viewer's details for the failing check, as the inspector draws them: two lines of identity, a
 * text index, the comparison first, the source line that asserted it, the file it left, and the exception,
 * response and attributes folded at the end because the comparison already says what they would.
 */

const source = {
  file: 'FailureDrills.cs',
  line: 36,
  method: 'Northstar.ProtoTest.FailureDrills.ARealWaitDoesNotCloseTheDueWindow',
  lines: [
    [33, 'await Task.Delay(TimeSpan.FromSeconds(1));'],
    [34, ''],
    [35, 'using var organization = await Proto.Context.Rest().GetAsync("/api/v1/organization");'],
    [36, 'organization'],
    [37, '    .Should.HaveHttpStatus(HttpStatusCode.OK)'],
    [38, '    .Should.MatchShape(new { status = SubscriptionStatuses.PastDue });'],
    [39, '}'],
  ] as const,
};

function tone(token: string): CSSProperties {
  return {'--node-color': `var(${token})`} as CSSProperties;
}

/** The index opens a folded part before it jumps to it, as the inspector does. */
function open(id: string): void {
  const target = document.getElementById(id);
  if (target instanceof HTMLDetailsElement) target.open = true;
}

export default function CheckView(): ReactNode {
  const id = useId();
  return (
    <div className={styles.checkView}>
      <p className={styles.crumb}>
        Test execution / <span>REST · GET /api/v1/organization</span>
      </p>
      <header className={styles.checkHead} title="assert.json.shape from ProtoTest.Rest, execution phase">
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
        <span className={styles.meta}>2.2 ms</span>
        <span className={styles.meta}>+1.15 s into the test</span>
      </p>

      <nav className={styles.vIndex} aria-label="Parts of this operation">
        <a className={styles.vIndexHot} href={`#${id}-comparison`}>
          Comparison
        </a>
        <a href={`#${id}-source`}>Source</a>
        <a href={`#${id}-evidence`}>Evidence</a>
        <a href={`#${id}-attributes`} onClick={() => open(`${id}-attributes`)}>
          Attributes
        </a>
      </nav>

      <section className={styles.vCard} id={`${id}-comparison`}>
        <header>
          <strong>Validated document</strong>
          <small>
            <b className={styles.matched}>✓</b> matched <b className={styles.differs}>×</b> expected, then
            actual
          </small>
        </header>
        <div className={styles.mismatch}>
          <i>×</i> <span className={styles.property}>&quot;status&quot;</span>
          <span className={styles.colon}>:</span> <s>&quot;past_due&quot;</s> →{' '}
          <strong>&quot;active&quot;</strong>
        </div>
      </section>
      <details className={styles.vFold}>
        <summary>The response it judged</summary>
        <pre className={styles.vFoldBody}>
          {'{ "status": "active", "planId": "growth", "seatCount": 1, ... }'}
        </pre>
      </details>

      <section className={`${styles.vCard} ${styles.source}`} id={`${id}-source`}>
        <header>
          <strong className={styles.vMono}>
            {source.file}:{source.line}
          </strong>
          <small>{source.method}</small>
        </header>
        <pre>
          {source.lines.map(([number, text]) => (
            <span key={number} className={number === source.line ? styles.current : undefined}>
              <b>{number}</b>
              {text || ' '}
            </span>
          ))}
        </pre>
      </section>

      <section className={styles.vBlock} id={`${id}-evidence`}>
        <h4>Evidence</h4>
        <div className={styles.vFileRow}>
          <span>File</span>
          <strong>597539000012-rest-01-expected-shape</strong>
          <small>application/json, 21 B</small>
        </div>
      </section>

      <details className={styles.vFold}>
        <summary>
          Exception <small>JsonShapeMismatchException</small>
        </summary>
        <p className={styles.vFoldBody}>
          Shape mismatch failed with 1 error(s): [$.status]: Values did not match. (Expected:
          &quot;past_due&quot;, Actual: &quot;active&quot;)
        </p>
      </details>
      <details className={styles.vFold} id={`${id}-attributes`}>
        <summary>
          Attributes <small>12</small>
        </summary>
        <p className={styles.vFoldBody}>
          The shape check&apos;s twelve recorded attributes; open the run in the viewer to read them.
        </p>
      </details>
    </div>
  );
}
