import {useState, type ReactNode} from 'react';

import styles from './styles.module.css';

/*
 * The demo run as the viewer opens it: the verdict and its strip, then one view at a time. Overview holds what
 * needs attention and what the run could see; Timeline and Details are the run's clock and identity.
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

const outcomes: Tick[] = Array.from({length: 19}, (_, index) => {
  const test = index + 1;
  if (test === 8 || test === 10 || test === 12 || test === 15) return 'failed';
  if (test === 11) return 'partial';
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
    number: '08',
    title: 'A bare status hides what the application said',
    kicker: 'Assert status · 201 Created',
    detail: 'Expected HTTP status 201 (Created), but received 400 (BadRequest).',
    outcome: 'failed',
    label: 'Assertion',
  },
  {
    number: '10',
    title: 'An unknown project ID is treated as mine',
    kicker: 'Assert status · 200 OK',
    detail: 'Expected HTTP status 200 (OK), but received 404 (NotFound).',
    outcome: 'failed',
    label: 'Assertion',
  },
  {
    number: '12',
    title: 'A real wait does not close the due window',
    kicker: 'Assert response shape',
    detail: '$.status: expected "past_due", got "active"',
    outcome: 'failed',
    label: 'Assertion',
  },
  {
    number: '15',
    title: 'The address was hardcoded for one machine',
    kicker: 'Test execution',
    detail: 'ConnectionError reaching http://127.0.0.1:5099: connection refused.',
    outcome: 'failed',
    label: 'Runner failure',
  },
  {
    number: '11',
    title: 'A passing journey can still carry a warning',
    kicker: 'The create response carried 4 fields no assertion mentioned: createdAtUtc, environmentCount, id, slug.',
    detail: 'Warning, Coverage',
    outcome: 'partial',
    label: 'Finding',
  },
  {
    number: '11',
    title: 'The create response carried 4 fields no assertion mentioned: createdAtUtc, environmentCount, id, slug.',
    kicker: 'Coverage',
    detail: '',
    outcome: 'partial',
    label: 'Warning finding',
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

const timeline = [
  {number: '08', title: 'A bare status hides what the application said', phases: [['setup', 1741.8, 44.1041], ['execution', 1785.929, 35.6183], ['teardown', 1821.6, 58.9866]]},
  {number: '10', title: 'An unknown project ID is treated as mine', phases: [['setup', 1742.668, 46.3363], ['execution', 1789.032, 26.2419], ['teardown', 1815.337, 50.0184]]},
  {number: '12', title: 'A real wait does not close the due window', phases: [['setup', 1748.087, 46.3246], ['execution', 1794.436, 1108.6688], ['teardown', 2903.184, 7.3962]], gapStart: 1886.303, gapLength: 1006.522},
  {number: '15', title: 'The address was hardcoded for one machine', phases: [['setup', 1880.988, 37.3838], ['execution', 1918.419, 2028.7205], ['teardown', 3947.283, 10.3628]], gapStart: 1918.419, gapLength: 2028.7205},
];

type RunTab = 'overview' | 'timeline' | 'details';
const runTabs: [RunTab, string][] = [['overview', 'Overview'], ['timeline', 'Timeline'], ['details', 'Details']];

export default function RunView(): ReactNode {
  const [tab, setTab] = useState<RunTab>('overview');
  return (
    <div className={styles.run}>
      <p className={styles.outcomeLine}>
        <b className={styles.failedText}>4 failed</b>, <b className={styles.partialText}>1 partial</b>, 14 passed
      </p>
      <p className={styles.meta}>
        <span>19 tests in 4.06 s</span>
        <span>29 Sep 2026, 18:37:26 UTC</span>
        <span>.NET 8.0.31 on Microsoft Windows 10.0.26200</span>
        <span className={styles.file}>ProtoTest demo trace</span>
      </p>

      <div className={styles.bar} aria-hidden="true">
        {outcomes.map((outcome, index) => (
          <i key={index} className={`${styles.tick} ${tickClass[outcome]}`} />
        ))}
      </div>

      <div className={styles.runTabs} role="group" aria-label="Views of the run">
        {runTabs.map(([id, label]) => (
          <button key={id} type="button" aria-pressed={tab === id} className={`${styles.tab} ${tab === id ? styles.tabActive : ''}`} onClick={() => setTab(id)}>
            {label}
          </button>
        ))}
      </div>

      {tab === 'overview' && <>
      <section className={styles.panel}>
        <header className={styles.panelHead}>Needs attention</header>
        <div className={styles.attention}>
          {attention.map((row) => (
            <div key={`${row.number}-${row.title}`} className={`${styles.attRow} ${attClass[row.outcome]}`}>
              <span className={styles.attNumber}>{row.number}</span>
              <span className={styles.attBody}>
                <b className={styles.attTitle}>{row.title}</b>
                <span className={styles.attKicker}>
                  <span className={`${styles.attLabel} ${attLabelClass[row.outcome]}`}>{row.label}</span> {row.kicker}
                </span>
                {row.detail && <span className={styles.attDetail}>{row.detail}</span>}
              </span>
            </div>
          ))}
        </div>
      </section>
      <section className={styles.panel}>
        <header className={styles.panelHead}>What this run could see</header>
        <div className={styles.kv}><span className={styles.kvLabel}>Application</span><b>In-process</b></div>
        <div className={styles.kv}><span className={styles.kvLabel}>Capabilities</span><span className={styles.chips}>{capabilities.map(capability => <i key={capability} className={styles.chip}>{capability}</i>)}</span></div>
        <div className={styles.kv}><span className={styles.kvLabel}>Values from</span><span className={styles.chips}>
          <i className={`${styles.chip} ${styles.chipOn}`}>Test side</i><i className={`${styles.chip} ${styles.chipOff}`}>Observed (not visible)</i><i className={`${styles.chip} ${styles.chipOn}`}>Application</i>
        </span></div>
      </section>
      </>}
      {tab === 'timeline' && <section className={styles.panel}>
        <header className={styles.panelHead}>Run timeline</header>
        <p className={styles.panelLead}>Four test rows from the full list. Hatched time has no recorded operation.</p>
        <div className={styles.mockRuler}><span>start</span><span>4.06 s</span></div>
        {timeline.map(test => <div key={test.number} className={styles.clockRow}>
          <span>{test.number} {test.title}</span><span className={styles.clockTrack}>
            {test.phases.map(([phase, start, length]) => <i key={phase} style={{left: `${Number(start) / 4059.3055 * 100}%`, width: `${Number(length) / 4059.3055 * 100}%`, background: `var(--phase-${phase})`}} />)}
            {test.gapStart !== undefined && <i className={styles.gapBar} style={{left: `${test.gapStart / 4059.3055 * 100}%`, width: `${test.gapLength / 4059.3055 * 100}%`}} />}
          </span>
        </div>)}
      </section>}
      {tab === 'details' && <section className={styles.panel}>
        <header className={styles.panelHead}>Run details</header>
        <dl className={styles.runDetails}>
          <dt>Run id</dt><dd>b8f1c1c58d984319a2c90b05aa3f2d3e</dd>
          <dt>environment.os</dt><dd>Microsoft Windows 10.0.26200</dd>
          <dt>environment.osArchitecture</dt><dd>X64</dd>
          <dt>environment.processArchitecture</dt><dd>X64</dd>
          <dt>environment.runtime</dt><dd>.NET 8.0.31</dd>
        </dl>
      </section>}
    </div>
  );
}
