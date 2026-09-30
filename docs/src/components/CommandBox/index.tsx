import {useId, useState, type ReactNode} from 'react';

import styles from './styles.module.css';

export interface CommandBoxRunner {
  /** The template's runner id, e.g. `nunit`; the radio's value. */
  id: string;
  /** The visible label, e.g. `xUnit v3`. */
  label: string;
  /** The lines this runner copies, in order. */
  commands: string[];
}

interface CommandBoxProps {
  /** The label above the commands, saying what they do. */
  title: string;
  /** The lines the button copies, in order. */
  commands: string[];
  /** Runner variants the reader can switch between. The first is the one shown by default. */
  runners?: CommandBoxRunner[];
}

/*
 * The hero's first action: the commands a reader pastes into a terminal, with one button that copies them
 * as a block. With `runners`, a small radio row swaps the template's runner, so each reader sees the
 * commands for the runner they use. The visible label reports what happened, and a status line announces it.
 */
export default function CommandBox({title, commands, runners}: CommandBoxProps): ReactNode {
  const [state, setState] = useState<'idle' | 'copied' | 'failed'>('idle');
  const [runnerId, setRunnerId] = useState(runners?.[0]?.id ?? '');
  const baseId = useId();
  const lines = runners?.find((runner) => runner.id === runnerId)?.commands ?? commands;

  async function copy(): Promise<void> {
    try {
      await navigator.clipboard.writeText(lines.join('\n'));
      setState('copied');
    } catch {
      setState('failed');
    }
    window.setTimeout(() => setState('idle'), 2000);
  }

  return (
    <div className={styles.box} data-surface="blueprint">
      <div className={styles.head}>
        <span className={styles.title}>{title}</span>
        <button type="button" className={styles.copy} onClick={copy}>
          {state === 'copied' ? 'Copied' : state === 'failed' ? 'Copy failed' : 'Copy'}
          <span className={styles.status} role="status" aria-live="polite">
            {state === 'copied' ? 'Commands copied to the clipboard.' : ''}
          </span>
        </button>
      </div>
      {runners ? (
        <div className={styles.runners} role="group" aria-labelledby={`${baseId}-label`}>
          <span className={styles.runnerLabel} id={`${baseId}-label`}>
            Runner
          </span>
          {runners.map((runner) => (
            <label key={runner.id} className={styles.runner}>
              <input
                type="radio"
                name={`${baseId}-runner`}
                value={runner.id}
                checked={runner.id === runnerId}
                onChange={() => {
                  setRunnerId(runner.id);
                  setState('idle');
                }}
              />
              <span>{runner.label}</span>
            </label>
          ))}
        </div>
      ) : null}
      <pre className={styles.commands} tabIndex={0} aria-label="Starter commands">
        <code>{lines.join('\n')}</code>
      </pre>
    </div>
  );
}
