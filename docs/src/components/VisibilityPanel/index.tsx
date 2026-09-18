import type {ReactNode} from 'react';

import Frame from '@site/src/components/Frame';
import styles from './styles.module.css';

/*
 * The top of ProtoTrace's run screen for the bundled demo trace: the run in one sentence, one tick per test,
 * and what the run could see - including what it could not. Values are the demo trace's.
 */

// 37 tests in start order; the viewer numbers them the same way.
const outcomes = Array.from({length: 37}, (_, index) =>
  index === 16 ? 'failed' : index === 1 || index === 12 ? 'partial' : 'passed');

const capabilities = ['Data', 'GraphQL', 'REST', 'ASP.NET Core', 'SQL'];

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
            <span className={styles.failed}>1 failed</span>, <span className={styles.partial}>2 partial</span>, 34 passed
          </strong>
          <span className={styles.meta}>37 tests in 2.70 s</span>
        </>
      }
      foot={<>The run screen of the same trace. Absent sources keep their place, drawn dashed.</>}>
      <div className={styles.body}>
        <div className={styles.strip} role="img" aria-label="One tick per test: test 17 failed, tests 2 and 13 were partial">
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
