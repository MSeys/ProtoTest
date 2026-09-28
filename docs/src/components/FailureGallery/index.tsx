import {useState, type ReactNode} from 'react';
import Link from '@docusaurus/Link';

import Frame from '@site/src/components/Frame';
import {failureDrills, type DrillRecord} from '@site/src/data/failureDrills';
import {drillRun, type TraceSource} from '@site/src/data/traceSources';
import styles from './styles.module.css';

function Records({record}: {record: DrillRecord[]}): ReactNode {
  return (
    <ul className={styles.records}>
      {record.map((entry, index) => (
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
  );
}

interface FailureGalleryProps {
  /** The run the card values come from. The default is the sample's drill run. */
  source?: TraceSource;
}

/*
 * L0's spine: one card per failure mode, each opening the recorded slice from the drill run and the
 * test that does the same journey the right way. The numbers are the trace's, so a reader can open
 * the same run and check them.
 */
export default function FailureGallery({source = drillRun}: FailureGalleryProps): ReactNode {
  const [open, setOpen] = useState<Set<string>>(() => new Set(['time']));

  function toggle(id: string): void {
    setOpen((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  return (
    <Frame
      head={
        <>
          <strong>Four failures, four fixes</strong>
          <span className={styles.headMeta}>samples/Northstar.ProtoTest</span>
        </>
      }
      foot={
        <>
          Every line is from {source.what}
          {source.file && <> in <code>{source.file}</code></>}. The drill failed on purpose, and the
          paired test runs the same journey the right way. Set <code>ProtoTest__Sample__Drills=true</code>{' '}
          to record the drills in your own run.
        </>
      }>
      <div className={styles.grid}>
        {failureDrills.map((pair) => {
          const expanded = open.has(pair.id);
          return (
            <article key={pair.id} className={`${styles.card} ${expanded ? styles.cardOpen : ''}`}>
              <h3 className={styles.cardHead}>
                <button
                  type="button"
                  className={styles.toggle}
                  aria-label={`${pair.question}: ${pair.ask}`}
                  aria-expanded={expanded}
                  aria-controls={`failure-${pair.id}`}
                  onClick={() => toggle(pair.id)}>
                  <span className={styles.mode}>{pair.question}</span>
                  <span className={styles.ask}>{pair.ask}</span>
                  <code className={styles.test}>{pair.drill.test}</code>
                  <span className={styles.chevron} aria-hidden="true" />
                </button>
              </h3>

              {expanded && (
                <div className={styles.panel} id={`failure-${pair.id}`}>
                  <div className={styles.side}>
                    <div className={styles.sideHead}>
                      <span className={styles.sideLabel}>The drill</span>
                      <span className={styles.elapsed}>{pair.drill.elapsed}</span>
                    </div>
                    <p className={styles.what}>{pair.drill.what}</p>
                    <Records record={pair.drill.record} />
                  </div>

                  <div className={`${styles.side} ${styles.sideFix}`}>
                    <div className={styles.sideHead}>
                      <span className={styles.sideLabel}>The test that holds</span>
                      <span className={styles.elapsed}>{pair.fix.elapsed}</span>
                    </div>
                    <p className={styles.what}>{pair.fix.what}</p>
                    <Records record={pair.fix.record} />
                  </div>

                  <p className={styles.change}>
                    <strong>What changes</strong>
                    {pair.change}
                  </p>

                  {source.href && (
                    <Link className={styles.open} href={source.href}>
                      Open this run in the viewer
                    </Link>
                  )}
                </div>
              )}
            </article>
          );
        })}
      </div>
    </Frame>
  );
}
