import type {ReactNode} from 'react';

import styles from './styles.module.css';

/*
 * The viewer's Run view for the bundled demo: the outcome line, the phase bar with the four failures,
 * the "what this run could see" panel, and the needs-attention list. Values come from
 * prototest-demo.prototrace; the test list below needs-attention is left out on purpose - the failed
 * tests are the story here.
 */

type Tick = 'passed' | 'failed' | 'partial';

const tickClass: Record<Tick, string> = {
  passed: styles.tickPassed,
  failed: styles.tickFailed,
  partial: styles.tickPartial,
};

const attClass: Record<Tick, string> = {
  failed: styles.attFailed,
  partial: styles.attPartial,
  passed: styles.attPassed,
};

const attLabelClass: Record<Tick, string> = {
  failed: styles.attLabelFailed,
  partial: styles.attLabelPartial,
  passed: styles.attLabelPassed,
};

const outcomes: Tick[] = Array.from({length: 18}, (_, index) => {
  const test = index + 1;
  if (test === 1 || test === 2 || test === 4 || test === 12) return 'failed';
  return 'passed';
});

const capabilities = ['Playwright', 'Data', 'Sheets', 'GraphQL', 'REST', 'ASP.NET Core', 'SQL'];

interface Attention {
  number: string;
  title: string;
  kicker: string;
  detail: string;
  outcome: Tick;
  label: string;
}

const attention: Attention[] = [
  {
    number: '01',
    title: 'A real wait does not close the due window',
    kicker: 'Assert response shape',
    detail: '$.status: expected "past_due", got "active"',
    outcome: 'failed',
    label: 'Failed',
  },
  {
    number: '02',
    title: 'A bare status hides what the application said',
    kicker: 'Assert status · 201 Created',
    detail: 'Expected HTTP status 201 (Created), but received 400 (BadRequest).',
    outcome: 'failed',
    label: 'Failed',
  },
  {
    number: '04',
    title: 'An unknown project ID is treated as mine',
    kicker: 'Assert status · 200 OK',
    detail: 'Expected HTTP status 200 (OK), but received 404 (NotFound).',
    outcome: 'failed',
    label: 'Failed',
  },
  {
    number: '12',
    title: 'The address was hardcoded for one machine',
    kicker: 'Test execution',
    detail: 'ConnectionError reaching http://127.0.0.1:5099: connection refused.',
    outcome: 'failed',
    label: 'Failed',
  },
  {
    number: 'Gate',
    title: 'no error findings',
    kicker: '',
    detail: 'No error findings were recorded.',
    outcome: 'passed',
    label: 'Passed',
  },
];

export default function RunView(): ReactNode {
  return (
    <div className={styles.run}>
      <p className={styles.outcomeLine}>
        <b className={styles.failedText}>4 failed</b>,{' '}
        <b className={styles.passedText}>14 passed</b>
      </p>
      <p className={styles.meta}>18 tests in 5.15 s · 29 sep 2026, 17:52:38</p>
      <p className={styles.meta}>.NET 8.0.31 on Microsoft Windows 10.0.26200 · ProtoTest demo trace</p>

      <div className={styles.bar} aria-hidden="true">
        {outcomes.map((outcome, index) => (
          <i key={index} className={`${styles.tick} ${tickClass[outcome]}`} />
        ))}
      </div>

      <section className={styles.panel}>
        <header className={styles.panelHead}>What this run saw</header>
        <p className={styles.panelLead}>Where the app ran, which capabilities were active, and what the trace recorded.</p>
        <div className={styles.kv}>
          <span className={styles.kvLabel}>Application</span>
          <b>In-process</b>
        </div>
        <div className={styles.kv}>
          <span className={styles.kvLabel}>Capabilities</span>
          <span className={styles.chips}>
            {capabilities.map((capability) => (
              <i key={capability} className={styles.chip}>
                {capability}
              </i>
            ))}
          </span>
        </div>
        <div className={styles.kv}>
          <span className={styles.kvLabel}>Values from</span>
          <span className={styles.chips}>
            <i className={`${styles.chip} ${styles.chipOn}`}>Test side</i>
            <i className={`${styles.chip} ${styles.chipOff}`}>Observed (not visible)</i>
            <i className={`${styles.chip} ${styles.chipOn}`}>Application</i>
          </span>
        </div>
      </section>

      <section className={styles.panel}>
        <header className={styles.panelHead}>Needs attention</header>
        <p className={styles.panelLead}>Failed tests first, then run findings and gate results.</p>
        <div className={styles.attention}>
          {attention.map((row) => (
            <div key={`${row.number}-${row.title}`} className={`${styles.attRow} ${attClass[row.outcome]}`}>
              <span className={styles.attNumber}>{row.number}</span>
              <span className={styles.attBody}>
                <b className={styles.attTitle}>{row.title}</b>
                {row.kicker && <span className={styles.attKicker}>{row.kicker}</span>}
                <span className={styles.attDetail}>{row.detail}</span>
              </span>
              <span className={`${styles.attLabel} ${attLabelClass[row.outcome]}`}>{row.label}</span>
            </div>
          ))}
        </div>
      </section>
    </div>
  );
}
