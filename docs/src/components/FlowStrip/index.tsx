import type {CSSProperties, ReactNode} from 'react';
import Frame from '@site/src/components/Frame';
import styles from './styles.module.css';

export interface FlowStep {
  title: ReactNode;
  /** One short line under the title: what happens there, or what it hands on. */
  detail?: ReactNode;
  /** `success` or `danger` marks an end state; most steps are neutral. */
  tone?: 'neutral' | 'success' | 'danger';
}

interface FlowStripProps {
  steps: FlowStep[];
  /** A line under the steps for what does not fit the line: a branch, a way back, an exception. */
  note?: ReactNode;
  caption?: ReactNode;
}

/*
 * A few steps in order, numbered, with an arrow from each to the next: in a row when the frame is wide and the
 * steps are few, in a column otherwise, so the labels never shrink. It replaces the docs' linear mermaid flowcharts. Steps are an
 * ordered list, so a screen reader reads the order the arrows draw.
 */
// More steps than this do not fit a docs column side by side without breaking words, so they stay a column.
const MAX_ROW = 4;

export default function FlowStrip({steps, note, caption}: FlowStripProps): ReactNode {
  return (
    <Frame foot={caption}>
      <div className={styles.body}>
        <ol
          className={`${styles.steps} ${steps.length <= MAX_ROW ? styles.rowable : ''}`}
          style={{'--steps': steps.length} as CSSProperties}>
          {steps.map((step, index) => (
            <li key={index} className={`${styles.step} ${styles[step.tone ?? 'neutral']}`}>
              <span className={styles.number} aria-hidden="true">
                {index + 1}
              </span>
              <span className={styles.title}>{step.title}</span>
              {step.detail && <span className={styles.detail}>{step.detail}</span>}
            </li>
          ))}
        </ol>
        {note && <p className={styles.note}>{note}</p>}
      </div>
    </Frame>
  );
}
