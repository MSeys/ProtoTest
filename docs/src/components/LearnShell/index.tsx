import Link from '@docusaurus/Link';
import type {ReactNode} from 'react';

import Checkpoint, {type CheckpointContent} from '@site/src/components/Checkpoint';
import styles from './styles.module.css';

export interface LearnNext {
  label: string;
  to: string;
  note: string;
}

interface LearnShellProps {
  /** Where the lesson sits in the track, e.g. "Level 0, lesson 1". */
  level: string;
  /** The reading time the hub promises, e.g. "About 15 minutes". */
  minutes: string;
  /** What the reader can do when the lesson ends. */
  outcome: string[];
  /** The lesson it builds on and anything to install, in the reader's words. */
  before: ReactNode[];
  /** The real failure or question the lesson starts from, from the Learning demo. */
  situation: ReactNode;
  /** The question, the step that proves it, and the answer behind the reveal. */
  checkpoint: CheckpointContent;
  /** Two or three lines the reader should take away. */
  learned: string[];
  /** The next lesson or the reference page behind this one. */
  next: LearnNext[];
  /** The walkthrough: prose, snippets and trace components. */
  children: ReactNode;
}

/*
 * The lesson frame every Learn page uses: outcome, prerequisites, the situation, the walkthrough, the
 * checkpoint, what the lesson taught and what to read next. The shape is the one the hub promises in
 * "How a lesson works", so a lesson never invents its own order.
 */
export default function LearnShell({
  level,
  minutes,
  outcome,
  before,
  situation,
  checkpoint,
  learned,
  next,
  children,
}: LearnShellProps): ReactNode {
  return (
    <section className={styles.shell}>
      <header className={styles.head}>
        <span className={styles.level}>{level}</span>
        <span className={styles.minutes}>{minutes}</span>
      </header>

      <div className={styles.duo}>
        <section>
          <h2 className={styles.label}>
            <span className={styles.step}>01</span>Outcome
          </h2>
          <ul className={styles.outcome}>
            {outcome.map((item) => (
              <li key={item}>{item}</li>
            ))}
          </ul>
        </section>
        <section>
          <h2 className={styles.label}>
            <span className={styles.step}>02</span>Before you start
          </h2>
          <ul className={styles.before}>
            {before.map((item, index) => (
              <li key={index}>{item}</li>
            ))}
          </ul>
        </section>
      </div>

      <section>
        <h2 className={styles.label}>
          <span className={styles.step}>03</span>The situation
        </h2>
        <div className={styles.body}>{situation}</div>
      </section>

      <section>
        <h2 className={styles.label}>
          <span className={styles.step}>04</span>The walkthrough
        </h2>
        <div className={styles.body}>{children}</div>
      </section>

      <section>
        <h2 className={styles.label}>
          <span className={styles.step}>05</span>Checkpoint
        </h2>
        <Checkpoint {...checkpoint} />
      </section>

      <section>
        <h2 className={styles.label}>
          <span className={styles.step}>06</span>What you learned
        </h2>
        <ul className={styles.learned}>
          {learned.map((item) => (
            <li key={item}>{item}</li>
          ))}
        </ul>
      </section>

      <section>
        <h2 className={styles.label}>
          <span className={styles.step}>07</span>Where to go next
        </h2>
        <div className={styles.next}>
          {next.map((item) => (
            <Link key={item.to} className={styles.card} to={item.to}>
              <strong>{item.label}</strong>
              <span>{item.note}</span>
            </Link>
          ))}
        </div>
      </section>
    </section>
  );
}
