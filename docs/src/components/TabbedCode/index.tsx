import {useId, useRef, useState, type KeyboardEvent, type ReactNode} from 'react';

import CodeSnippet from '@site/src/components/CodeSnippet';
import Frame from '@site/src/components/Frame';
import styles from './styles.module.css';

export interface CodeTab {
  id: string;
  label: string;
  filename: string;
  code: string;
  /** The Prism language for the snippet; C# unless the tab holds something else. */
  language?: string;
  footnote?: string;
}

interface TabbedCodeProps {
  tabs: CodeTab[];
  /** The accessible name of the tab list. */
  label?: string;
}

/*
 * Snippets behind underline tabs. The tab bar scrolls sideways when there are more tabs than fit, the file name
 * sits on its own bar above the code, and the code keeps its lines whole and scrolls inside itself - so a
 * phone shows the snippet as written instead of rewrapping it. Arrow keys, Home and End move between tabs.
 */
export default function TabbedCode({tabs, label}: TabbedCodeProps): ReactNode {
  const [activeId, setActiveId] = useState(tabs[0].id);
  const baseId = useId();
  const tabRefs = useRef(new Map<string, HTMLButtonElement>());
  const active = tabs.find((tab) => tab.id === activeId)!;

  function move(event: KeyboardEvent<HTMLDivElement>): void {
    const index = tabs.findIndex((tab) => tab.id === activeId);
    let next = index;
    if (event.key === 'ArrowLeft') next = (index - 1 + tabs.length) % tabs.length;
    else if (event.key === 'ArrowRight') next = (index + 1) % tabs.length;
    else if (event.key === 'Home') next = 0;
    else if (event.key === 'End') next = tabs.length - 1;
    else return;

    event.preventDefault();
    const target = tabs[next]!;
    setActiveId(target.id);
    tabRefs.current.get(target.id)?.focus();
  }

  return (
    <Frame
      head={
        <div className={styles.tabs} role="tablist" aria-label={label} onKeyDown={move}>
          {tabs.map((tab) => (
            <button
              key={tab.id}
              type="button"
              role="tab"
              id={`${baseId}-${tab.id}`}
              aria-controls={`${baseId}-panel`}
              aria-selected={tab.id === activeId}
              tabIndex={tab.id === activeId ? 0 : -1}
              ref={(node) => {
                if (node) tabRefs.current.set(tab.id, node);
                else tabRefs.current.delete(tab.id);
              }}
              className={`${styles.tab} ${tab.id === activeId ? styles.tabActive : ''}`}
              onClick={() => setActiveId(tab.id)}>
              {tab.label}
            </button>
          ))}
        </div>
      }
      foot={active.footnote}>
      <div
        className={styles.body}
        id={`${baseId}-panel`}
        role="tabpanel"
        aria-labelledby={`${baseId}-${activeId}`}>
        <div className={styles.filename}>{active.filename}</div>
        <CodeSnippet code={active.code} language={active.language} scroll />
      </div>
    </Frame>
  );
}
