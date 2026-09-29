import {useState, type ReactNode} from 'react';

import styles from './styles.module.css';

interface CommandBoxProps {
  /** The label above the commands, saying what they do. */
  title: string;
  /** The lines the button copies, in order. */
  commands: string[];
}

/*
 * The hero's first action: the commands a reader pastes into a terminal, with one button that copies them
 * as a block. The visible label reports what happened, and a status line announces it.
 */
export default function CommandBox({title, commands}: CommandBoxProps): ReactNode {
  const [state, setState] = useState<'idle' | 'copied' | 'failed'>('idle');

  async function copy(): Promise<void> {
    try {
      await navigator.clipboard.writeText(commands.join('\n'));
      setState('copied');
    } catch {
      setState('failed');
    }
    window.setTimeout(() => setState('idle'), 2000);
  }

  return (
    <div className={styles.box}>
      <div className={styles.head}>
        <span className={styles.title}>{title}</span>
        <button type="button" className={styles.copy} onClick={copy}>
          {state === 'copied' ? 'Copied' : state === 'failed' ? 'Copy failed' : 'Copy'}
          <span className={styles.status} role="status" aria-live="polite">
            {state === 'copied' ? 'Commands copied to the clipboard.' : ''}
          </span>
        </button>
      </div>
      <pre className={styles.commands}>
        <code>{commands.join('\n')}</code>
      </pre>
    </div>
  );
}
