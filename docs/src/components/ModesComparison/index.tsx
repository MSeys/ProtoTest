import type {ReactNode} from 'react';

import Frame from '@site/src/components/Frame';
import styles from './styles.module.css';

/*
 * The three Level 5 modes side by side. One suite, three owners of the product:
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
    <Frame
      head={
        <>
          <strong>Three modes, one suite</strong>
          <span className={styles.headMeta}>Level 5 comparison</span>
        </>
      }
      foot={
        <>
          Container mode runs the full 75 including the seven Chromium journeys. Topology and
          published skip the same 13 clock-gated and in-process-gated journeys, so their counts
          match. Each lesson quotes its own run log beside its counts.
        </>
      }>
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
              <td>{row.api}</td>
              <td>{row.workers}</td>
              <td>
                {row.store}; {row.broker.charAt(0).toLowerCase() + row.broker.slice(1)}
              </td>
              <td>{row.counts}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </Frame>
  );
}
