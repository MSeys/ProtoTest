import type {ReactNode} from 'react';
import Frame from '@site/src/components/Frame';
import styles from './styles.module.css';

export interface CompareRun {
  /** "Passing run", "Failing run". */
  label: string;
  outcome: 'succeeded' | 'failed';
  /** The recorded values, in the order the reader should read them. */
  fields: Array<[name: string, value: string]>;
}

interface RunCompareProps {
  /** The two runs, the passing one first. */
  runs: [CompareRun, CompareRun];
  /** The field that explains the difference: marked in both runs. */
  focus: string;
  /** One sentence about the marked field. */
  note: ReactNode;
  /** Where the values come from: the trace and the attachment. */
  source: ReactNode;
}

/*
 * The same recorded values from two runs of one test, side by side, with the field that explains the difference
 * marked in both. Drawn like TraceDiff's panes: the passing run keeps the success edge, the failing one the danger
 * edge. The values are text, so a reader can select and search them.
 */
export default function RunCompare({runs, focus, note, source}: RunCompareProps): ReactNode {
  return (
    <Frame foot={source}>
      <div className={styles.body}>
        <div className={styles.runs}>
          {runs.map((run) => (
            <section key={run.label} className={`${styles.run} ${styles[run.outcome]}`} aria-label={run.label}>
              <header className={styles.head}>
                <span className={styles.label}>{run.label}</span>
                <span className={styles.outcome}>{run.outcome}</span>
              </header>
              <dl className={styles.fields}>
                {run.fields.map(([name, value]) => (
                  <div key={name} className={name === focus ? `${styles.field} ${styles.focus}` : styles.field}>
                    <dt>{name}</dt>
                    <dd>{value}</dd>
                  </div>
                ))}
              </dl>
            </section>
          ))}
        </div>
        <p className={styles.note}>{note}</p>
      </div>
    </Frame>
  );
}
