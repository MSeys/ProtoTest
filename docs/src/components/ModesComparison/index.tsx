import type {ReactNode} from 'react';

import Frame from '@site/src/components/Frame';
import styles from './styles.module.css';

/*
 * The three recorded modes side by side. One suite, three owners of the product:
 * the test process, the AppHost, or processes started outside the suite. Counts
 * are the runs the lessons quote: container 75/0, topology 61/13, published 61/13.
 */

const rows = [
  {
    mode: 'Containers',
    api: 'In the test process',
    workers: 'In the test process',
    store: 'Postgres container the run starts',
    broker: 'RabbitMQ container the run starts',
    counts: '75 passed, 0 skipped of 75',
  },
  {
    mode: 'Aspire topology',
    api: 'AppHost api project resource',
    workers: 'AppHost project resources',
    store: 'AppHost Postgres container',
    broker: 'AppHost RabbitMQ container',
    counts: '61 passed, 13 skipped of 74',
  },
  {
    mode: 'Published',
    api: 'A process started outside the suite',
    workers: 'Processes started outside the suite',
    store: 'Persistent container the run points at',
    broker: 'Persistent container the run points at',
    counts: '61 passed, 13 skipped of 74',
  },
];

export default function ModesComparison(): ReactNode {
  return (
    <Frame kind="figure"
      head={
        <>
          <strong>Three modes, one suite</strong>
          <span className={styles.headMeta}>Recorded mode comparison</span>
        </>
      }
      foot={
        <>
          These rows quote separate recorded runs. The container run had 75 tests. The earlier
          topology and published runs had 74. Both earlier runs skipped 13 journeys. Each lesson
          names its source log. Your counts depend on the checkout and configuration.
        </>
      }>
      <div className={styles.scroll} tabIndex={0} role="region" aria-label="Recorded mode comparison">
      <table className={styles.table}>
        <thead>
          <tr>
            <th>Mode</th>
            <th>API</th>
            <th>Workers</th>
            <th>Store and broker</th>
            <th>Counts</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.mode}>
              <td>
                <strong>{row.mode}</strong>
              </td>
              <td data-label="API">{row.api}</td>
              <td data-label="Workers">{row.workers}</td>
              <td data-label="Store and broker">
                {row.store}; {row.broker}
              </td>
              <td data-label="Counts">{row.counts}</td>
            </tr>
          ))}
        </tbody>
      </table>
      </div>
    </Frame>
  );
}
