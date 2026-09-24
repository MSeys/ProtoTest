import type {ReactNode} from 'react';

import styles from './styles.module.css';

/*
 * The viewer's Run view for the bundled demo: the outcome line, the phase bar with the two failures and
 * two partials, the "what this run could see" panel, and the needs-attention list. Values come from
 * prototest-demo.prototrace; the test list below needs-attention is left out on purpose - the failed
 * tests are the story here.
 */

type Tick = 'passed' | 'failed' | 'partial';
type AttentionOutcome = Tick | 'finding';

const tickClass: Record<Tick, string> = {
  passed: styles.tickPassed,
  failed: styles.tickFailed,
  partial: styles.tickPartial,
};

const attClass: Record<AttentionOutcome, string> = {
  failed: styles.attFailed,
  partial: styles.attPartial,
  finding: styles.attFinding,
  passed: styles.attPassed,
};

const attLabelClass: Record<AttentionOutcome, string> = {
  failed: styles.attLabelFailed,
  partial: styles.attLabelPartial,
  finding: styles.attLabelFinding,
  passed: styles.attLabelPassed,
};

const outcomes: Tick[] = Array.from({length: 44}, (_, index) => {
  const test = index + 1;
  if (test === 29 || test === 36) return 'failed';
  if (test === 13 || test === 23) return 'partial';
  return 'passed';
});

const capabilities = ['Playwright', 'Data', 'Sheets', 'GraphQL', 'REST', 'gRPC', 'ASP.NET Core', 'Northstar standalone', 'SQL'];

interface Attention {
  number: string;
  title: string;
  kicker: string;
  detail: string;
  outcome: AttentionOutcome;
  label: string;
}

const attention: Attention[] = [
  {
    number: '29',
    title: 'The dashboard never shows another tenants plan',
    kicker: 'Assert · Plan should have text "Enterprise"',
    detail: "Element 'DashboardPage.Plan' should have text \"Enterprise\" within 00:00:03. Last observed: text was \"Free\".",
    outcome: 'failed',
    label: 'Failed',
  },
  {
    number: '36',
    title: 'The organization reports its plan and project count',
    kicker: 'Assert response shape',
    detail: '$.projectCount: expected 99, got 0, and 1 more',
    outcome: 'failed',
    label: 'Failed',
  },
  {
    number: '13',
    title: 'A failed operation records its diagnostics and the run continues',
    kicker: 'Deliver subscription webhook',
    detail: 'The billing ledger did not acknowledge the webhook within 2 seconds.',
    outcome: 'partial',
    label: 'Partial',
  },
  {
    number: '23',
    title: 'Shape mismatches are captured without failing the run',
    kicker: 'Assert response shape',
    detail: '$.projectCount: expected 99, got 0, and 1 more',
    outcome: 'partial',
    label: 'Partial',
  },
  {
    number: '17',
    title: 'Deployment 1.0.0 took 1 deploy-minutes.',
    kicker: 'Delivery, delivery-budget',
    detail: 'Warning finding',
    outcome: 'finding',
    label: 'Warning finding',
  },
  {
    number: '',
    title: 'no error findings',
    kicker: 'Gate',
    detail: 'No error findings were recorded.',
    outcome: 'passed',
    label: 'Passed',
  },
];

export default function RunView(): ReactNode {
  return (
    <div className={styles.run}>
      <p className={styles.outcomeLine}>
        <b className={styles.failedText}>2 failed</b>, <b className={styles.partialText}>2 partial</b>,{' '}
        <b className={styles.passedText}>40 passed</b>
      </p>
      <p className={styles.meta}>44 tests in 7.40 s · 20 sep 2026, 12:32:18</p>
      <p className={styles.meta}>.NET 8.0.31 on Microsoft Windows 10.0.26200 · ProtoTest demo trace</p>

      <div className={styles.bar} aria-hidden="true">
        {outcomes.map((outcome, index) => (
          <i key={index} className={`${styles.tick} ${tickClass[outcome]}`} />
        ))}
      </div>

      <section className={styles.panel}>
        <header className={styles.panelHead}>What this run could see</header>
        <p className={styles.panelLead}>Where the application ran, what was composed, and how deep the trace reached.</p>
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
        <p className={styles.panelLead}>Failing and partial tests first, then what the run itself found and how its gates judged it.</p>
        <div className={styles.attention}>
          {attention.map((row) => (
            <div key={`${row.number}-${row.title}`} className={`${styles.attRow} ${attClass[row.outcome]}`}>
              <span className={styles.attNumber}>{row.number}</span>
              <span className={styles.attBody}>
                <b className={styles.attTitle}>{row.title}</b>
                <span className={styles.attKicker}>{row.kicker}</span>
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
