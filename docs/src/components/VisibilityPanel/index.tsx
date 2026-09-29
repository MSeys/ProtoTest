import type {ReactNode} from 'react';

import Frame from '@site/src/components/Frame';
import styles from './styles.module.css';

/*
 * The top of ProtoTrace's run screen for the bundled demo trace: the run in one sentence, one tick per test,
 * and what the run could see - including what it could not. Values are the demo trace's.
 */

// 18 tests in start order; the viewer numbers them the same way.
type Tick = 'passed' | 'failed' | 'partial';

const outcomes: Tick[] = Array.from({length: 18}, (_, index) => {
  const test = index + 1;
  if (test === 1 || test === 2 || test === 4 || test === 12) return 'failed';
  return 'passed';
});

const capabilities = ['Playwright', 'Data', 'Sheets', 'GraphQL', 'REST', 'ASP.NET Core', 'SQL'];

const sources = [
  {label: 'Test side', seen: true},
  {label: 'Observed', seen: false},
  {label: 'Application', seen: true},
];

export default function VisibilityPanel(): ReactNode {
  return (
    <Frame
      head={
        <>
          <strong className={styles.verdict}>
            <span className={styles.failed}>4 failed</span>, 14 passed
          </strong>
          <span className={styles.meta}>18 tests in 5.15 s</span>
        </>
      }
      foot={<>The run screen of the same trace. Absent sources keep their place, drawn dashed.</>}>
      <div className={styles.body}>
        <div className={styles.strip} role="img" aria-label="One tick per test: tests 1, 2, 4 and 12 failed">
          {outcomes.map((outcome, index) => (
            <i key={index} className={styles[outcome]} />
          ))}
        </div>

        <h3 className={styles.heading}>What this run could see</h3>
        <dl className={styles.facts}>
          <div>
            <dt>Application</dt>
            <dd>In-process</dd>
          </div>
          <div>
            <dt>Capabilities</dt>
            <dd className={styles.chips}>
              {capabilities.map((capability) => (
                <span key={capability} className={styles.capability}>
                  {capability}
                </span>
              ))}
            </dd>
          </div>
          <div>
            <dt>Values from</dt>
            <dd className={styles.chips}>
              {sources.map((source) => (
                <span key={source.label} className={`${styles.source} ${source.seen ? '' : styles.absent}`}>
                  <i aria-hidden="true" />
                  {source.label}
                </span>
              ))}
            </dd>
          </div>
        </dl>
      </div>
    </Frame>
  );
}
