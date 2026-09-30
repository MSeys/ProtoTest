import {useEffect, useRef, useState, type ReactNode} from 'react';
import styles from './styles.module.css';

/** Clipboard feedback resets on a new snippet and clears its timer when the control leaves the page. */
export default function CopyCode({text, label = 'Copy code'}: {text: string; label?: string}): ReactNode {
  const [state, setState] = useState<'idle' | 'copied' | 'failed'>('idle');
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  useEffect(() => {
    setState('idle');
    return () => clearTimeout(timer.current);
  }, [text]);
  async function copy(): Promise<void> {
    clearTimeout(timer.current);
    try {
      await navigator.clipboard.writeText(text.trim());
      setState('copied');
    } catch {
      setState('failed');
    }
    timer.current = setTimeout(() => setState('idle'), 2000);
  }
  return (
    <>
      <button type="button" className={styles.copy} onClick={copy}>
        {state === 'copied' ? 'Copied' : state === 'failed' ? 'Copy failed' : label}
      </button>
      <span className={styles.status} role="status">
        {state === 'copied'
          ? 'Copied to the clipboard.'
          : state === 'failed'
            ? 'Could not copy. Select the code to copy it manually.'
            : ''}
      </span>
    </>
  );
}
