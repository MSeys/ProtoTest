import {useState, type ReactNode} from 'react';
import Link from '@docusaurus/Link';

import Frame from '@site/src/components/Frame';
import {failureDrills, type DrillRecord} from '@site/src/data/failureDrills';
import {drillPairs} from '@site/src/data/traceSources';
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

/*
 * L0's spine: one card per failure mode, each opening the recorded slice from the drill run and the
 * test that does the same journey the right way. Each card links the drill's and the fix's own
 * archive, so a reader can open the same test and check what it recorded.
 */
export default function FailureGallery(): ReactNode {
  const [open, setOpen] = useState<Set<string>>(() => new Set<string>());

  function toggle(id: string): void {
    setOpen((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  return (
    <Frame kind="figure"
      head={
        <>
          <strong>What a failure looks like</strong>
        </>
      }
      foot={
        <>
          Each card is a real recorded run. The drill failed on purpose, and the paired test runs the
          same journey the right way. Each card links its own archive.
        </>
      }>
      <p className={styles.legend}>
        Each row reads kind, name, status, detail. The status marks what the check decided.
      </p>
      <div className={styles.grid}>
        {failureDrills.map((pair) => {
          const expanded = open.has(pair.id);
          const archives = drillPairs[pair.id];
          return (
            <article key={pair.id} className={`${styles.card} ${expanded ? styles.cardOpen : ''}`}>
              <h3 className={styles.cardHead} aria-label={`${pair.question}: ${pair.ask}`}>
                <button
                  type="button"
                  className={styles.toggle}
                  aria-label={`${pair.question}: ${pair.ask}`}
                  aria-expanded={expanded}
                  aria-controls={`failure-${pair.id}`}
                  onClick={() => toggle(pair.id)}>
                  <span className={styles.mode}>{pair.question}</span>
                  <span className={styles.ask}>{pair.ask}</span>
                  <span className={styles.verdict}>
                    <strong>Fix:</strong> {pair.change}
                  </span>
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
                    {archives.drill.href && (
                      <Link className={styles.open} href={archives.drill.href}>
                        Download the drill archive
                      </Link>
                    )}
                  </div>

                  <div className={`${styles.side} ${styles.sideFix}`}>
                    <div className={styles.sideHead}>
                      <span className={styles.sideLabel}>The fix</span>
                      <span className={styles.elapsed}>{pair.fix.elapsed}</span>
                    </div>
                    <p className={styles.what}>{pair.fix.what}</p>
                    <Records record={pair.fix.record} />
                    {archives.fix.href && (
                      <Link className={styles.open} href={archives.fix.href}>
                        Download the fix archive
                      </Link>
                    )}
                  </div>
                </div>
              )}
            </article>
          );
        })}
      </div>
    </Frame>
  );
}
