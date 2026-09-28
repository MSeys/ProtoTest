import {useId, useRef, useState, type KeyboardEvent, type ReactNode} from 'react';

import Frame from '@site/src/components/Frame';
import {failureDrills, type DrillSide} from '@site/src/data/failureDrills';
import {drillRun, type TraceSource} from '@site/src/data/traceSources';
import styles from './styles.module.css';

function Pane({id, role, side}: {id: string; role: string; side: DrillSide}): ReactNode {
  const failed = side.record.some((entry) => entry.status === 'failed');
  return (
    <section className={styles.pane} aria-labelledby={id}>
      <header className={styles.paneHead}>
        <span className={styles.role} id={id}>{role}</span>
        <span className={`${styles.verdict} ${failed ? styles.failed : styles.passed}`}>
          {failed ? 'failed' : 'succeeded'}
        </span>
      </header>
      <div className={styles.who}>
        <code className={styles.test}>{side.test}</code>
        <span className={styles.elapsed}>{side.elapsed}</span>
      </div>
      <ul className={styles.records}>
        {side.record.map((entry, index) => (
          <li key={index} className={entry.status === 'failed' ? styles.recordFailed : undefined}>
            <span className={styles.recordHead}>
              <code className={styles.kind}>{entry.kind}</code>
              <span className={styles.recordName}>{entry.name}</span>
              {entry.status && <span className={styles[entry.status]}>{entry.status}</span>}
            </span>
            {entry.detail && <span className={styles.detail}>{entry.detail}</span>}
          </li>
        ))}
      </ul>
    </section>
  );
}

interface TraceDiffProps {
  /** The run the paired values come from. The default is the sample's drill run. */
  source?: TraceSource;
}

/*
 * One journey, two runs: the drill on the left, the test that holds on the right, with the recorded
 * checks and durations in between. The pair selector walks the four questions.
 */
export default function TraceDiff({source = drillRun}: TraceDiffProps): ReactNode {
  const [activeId, setActiveId] = useState(failureDrills[0].id);
  const baseId = useId();
  const tabs = useRef<Array<HTMLButtonElement | null>>([]);
  const active = failureDrills.find((pair) => pair.id === activeId)!;

  const move = (event: KeyboardEvent<HTMLDivElement>) => {
    const key = event.key;
    if (key !== 'ArrowRight' && key !== 'ArrowLeft' && key !== 'Home' && key !== 'End') return;
    event.preventDefault();
    const index = failureDrills.findIndex((pair) => pair.id === activeId);
    const step = key === 'ArrowRight' ? 1 : key === 'ArrowLeft' ? -1 : 0;
    const nextIndex =
      key === 'Home'
        ? 0
        : key === 'End'
          ? failureDrills.length - 1
          : (index + step + failureDrills.length) % failureDrills.length;
    const next = failureDrills[nextIndex];
    if (!next) return;
    setActiveId(next.id);
    tabs.current[nextIndex]?.focus();
  };

  return (
    <Frame
      head={
        <div className={styles.tabs} role="tablist" aria-label="The four drill pairs" onKeyDown={move}>
          {failureDrills.map((pair, index) => (
            <button
              key={pair.id}
              type="button"
              role="tab"
              id={`${baseId}-${pair.id}`}
              ref={(element) => {
                tabs.current[index] = element;
              }}
              aria-selected={pair.id === activeId}
              aria-controls={`${baseId}-panel`}
              tabIndex={pair.id === activeId ? 0 : -1}
              className={`${styles.tab} ${pair.id === activeId ? styles.tabActive : ''}`}
              onClick={() => setActiveId(pair.id)}>
              {pair.question}
            </button>
          ))}
        </div>
      }
      foot={
        <>
          Every name, duration and message is from {source.what}
          {source.file && <> in <code>{source.file}</code></>}. The drill failed on purpose; the test
          beside it runs the same journey and passes.
        </>
      }>
      <div className={styles.panel} id={`${baseId}-panel`} role="tabpanel" aria-labelledby={`${baseId}-${active.id}`}>
        <p className={styles.ask}>{active.ask}</p>
        <div className={styles.panes}>
          <Pane id={`${baseId}-drill`} role="The drill" side={active.drill} />
          <Pane id={`${baseId}-fix`} role="The test that holds" side={active.fix} />
        </div>
        <p className={styles.change}>
          <strong>What changes</strong>
          {active.change}
        </p>
      </div>
    </Frame>
  );
}
