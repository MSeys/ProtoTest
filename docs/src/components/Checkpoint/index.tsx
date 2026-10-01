import {useId, useState, type ReactNode} from 'react';

import styles from './styles.module.css';

export interface CheckpointContent {
  /** The question to answer before anything is revealed. */
  question: ReactNode;
  /** The step that lets the reader judge the answer: a trace to open, a run to make, a value to check. */
  verify?: ReactNode;
  /** The answer, kept behind the reveal. A lesson in Markdown passes it as the children instead. */
  reveal?: ReactNode;
  children?: ReactNode;
}

/*
 * The lesson checkpoint: one question, the step that proves it, and the answer behind a reveal. The
 * answer is a button toggle rather than a details element, so it stays out of the tab order until the
 * reader asks for it.
 */
export default function Checkpoint({question, verify, reveal, children}: CheckpointContent): ReactNode {
  const [shown, setShown] = useState(false);
  const answerId = useId();

  return (
    <div className={styles.checkpoint}>
      <p className={styles.question}>{question}</p>

      {verify && (
        <div className={styles.verify}>
          <span className={styles.verifyLabel}>Verify</span>
          <div className={styles.verifyBody}>{verify}</div>
        </div>
      )}

      <div className={styles.answer}>
        <button
          type="button"
          className={styles.reveal}
          aria-expanded={shown}
          aria-controls={answerId}
          onClick={() => setShown((current) => !current)}>
          {shown ? 'Hide the answer' : 'Show the answer'}
        </button>
        <div id={answerId} hidden={!shown} className={styles.answerBody}>
          {children ?? reveal}
        </div>
      </div>
    </div>
  );
}
