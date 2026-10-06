import type {CSSProperties, ReactNode} from 'react';
import Frame from '@site/src/components/Frame';
import styles from './styles.module.css';

export type SequenceStep =
  | {
      from: string;
      to: string;
      label: ReactNode;
      /** A reply draws a dashed arrow back. */
      reply?: boolean;
      /** The message repeats; says until when. */
      repeat?: ReactNode;
    }
  | {note: ReactNode};

interface SequenceLanesProps {
  /** The lanes, left to right; steps name them by these strings. */
  participants: string[];
  steps: SequenceStep[];
  caption?: ReactNode;
}

/*
 * Who sends what to whom, in order: one lane per participant, one numbered row per message. A wide frame draws the
 * lanes and an arrow per message; a narrow one reads each row as "from → to", so no label ever shrinks. It replaces
 * the docs' mermaid sequence diagrams.
 */
export default function SequenceLanes({participants, steps, caption}: SequenceLanesProps): ReactNode {
  const lane = (name: string) => {
    const index = participants.indexOf(name);
    if (index < 0) throw new Error(`SequenceLanes: no participant named '${name}'.`);
    return index;
  };
  let number = 0;

  return (
    <Frame foot={caption}>
      <div className={styles.body} style={{'--lanes': participants.length} as CSSProperties}>
        <div className={styles.heads} aria-hidden="true">
          {participants.map((name) => (
            <span key={name} className={styles.head}>
              {name}
            </span>
          ))}
        </div>
        <div className={styles.lanes}>
          {participants.map((name, index) => (
            <span key={name} className={styles.lifeline} style={{'--lane': index} as CSSProperties} aria-hidden="true" />
          ))}
          <ol className={styles.rows}>
            {steps.map((step, index) => {
              if ('note' in step) {
                return (
                  <li key={index} className={styles.note}>
                    {step.note}
                  </li>
                );
              }
              number += 1;
              const from = lane(step.from);
              const to = lane(step.to);
              const direction = from === to ? styles.self : to > from ? styles.right : styles.left;
              const span = {
                '--start': Math.min(from, to) + 1,
                '--end': Math.max(from, to) + 2,
                '--span': Math.abs(to - from) + 1,
              } as CSSProperties;
              return (
                <li key={index} className={`${styles.message} ${direction} ${step.reply ? styles.reply : ''}`} style={span}>
                  <span className={styles.route}>{from === to ? step.from : `${step.from} → ${step.to}`}</span>
                  <span className={styles.label}>
                    <span className={styles.number}>{number}</span> {step.label}
                  </span>
                  {step.repeat && <span className={styles.repeat}>repeats {step.repeat}</span>}
                  <span className={styles.arrow} aria-hidden="true" />
                </li>
              );
            })}
          </ol>
        </div>
      </div>
    </Frame>
  );
}
