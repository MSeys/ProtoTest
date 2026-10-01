import type {ReactNode} from 'react';

import styles from './styles.module.css';

/*
 * The banner at the top of a lesson: where it sits in its track, how long it takes, what the reader will be able
 * to do, and what they need first. Everything else in a lesson is Markdown under it, with its own headings.
 */
interface LessonProps {
  track: string;
  /** The lesson's place in its track, such as "Lesson 2 of 4". */
  step: string;
  minutes: number;
  /** Two or three things the reader can do after the lesson. */
  outcomes: string[];
  /** What the lesson assumes: an earlier lesson, a tool. */
  needs?: ReactNode[];
}

export default function Lesson({track, step, minutes, outcomes, needs = []}: LessonProps): ReactNode {
  return (
    <aside className={styles.lesson} aria-label="About this lesson">
      <p className={styles.meta}>
        <strong>{track}</strong>
        <span>{step}</span>
        <span>About {minutes} minutes</span>
      </p>
      <div className={styles.columns}>
        <div>
          <p className={styles.label}>You will</p>
          <ul>
            {outcomes.map((outcome) => (
              <li key={outcome}>{outcome}</li>
            ))}
          </ul>
        </div>
        {needs.length > 0 && (
          <div>
            <p className={styles.label}>You need</p>
            <ul>
              {needs.map((need, index) => (
                <li key={index}>{need}</li>
              ))}
            </ul>
          </div>
        )}
      </div>
    </aside>
  );
}
