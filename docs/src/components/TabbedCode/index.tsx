import {useId, useRef, useState, type KeyboardEvent, type ReactNode} from 'react';

import CodeSnippet from '@site/src/components/CodeSnippet';
import CopyCode from '@site/src/components/CopyCode';
import Frame from '@site/src/components/Frame';
import styles from './styles.module.css';

export interface CodeTab {
  id: string;
  label: string;
  filename: string;
  code: string;
  language?: string;
  footnote?: string;
}

interface TabbedCodeProps {
  tabs: CodeTab[];
  label?: string;
}

/*
 * Snippets behind underline tabs. The tab bar scrolls sideways when there are more tabs than fit, the file name
 * sits on its own bar above the code, and the code keeps its lines whole and scrolls inside itself - so a
 * phone shows the snippet as written instead of rewrapping it.
 */
export default function TabbedCode({tabs, label}: TabbedCodeProps): ReactNode {
  const id = useId();
  const buttons = useRef<(HTMLButtonElement | null)[]>([]);
  const [activeId, setActiveId] = useState(tabs[0]?.id);
  const active = tabs.find((tab) => tab.id === activeId) ?? tabs[0];
  if (!active) return null;

  function activate(index: number): void {
    setActiveId(tabs[index].id);
  }

  function navigate(event: KeyboardEvent<HTMLButtonElement>, index: number): void {
    const next =
      event.key === 'ArrowRight'
        ? (index + 1) % tabs.length
        : event.key === 'ArrowLeft'
          ? (index - 1 + tabs.length) % tabs.length
          : event.key === 'Home'
            ? 0
            : event.key === 'End'
              ? tabs.length - 1
              : null;
    if (next === null) return;
    event.preventDefault();
    activate(next);
    buttons.current[next]?.focus();
  }

  return (
    <Frame
      kind="code"
      head={
        <>
          {tabs.length === 1 ? (
            <strong className={styles.title}>{active.label}</strong>
          ) : (
            <div className={styles.tabs} role="tablist" aria-label={label ?? 'Code examples'}>
              {tabs.map((tab, index) => (
                <button
                  key={tab.id}
                  type="button"
                  role="tab"
                  id={`${id}-tab-${tab.id}`}
                  aria-controls={`${id}-panel`}
                  tabIndex={tab.id === active.id ? 0 : -1}
                  ref={(button) => {
                    buttons.current[index] = button;
                  }}
                  className={`${styles.tab} ${tab.id === active.id ? styles.tabActive : ''}`}
                  onClick={() => activate(index)}
                  onKeyDown={(event) => navigate(event, index)}
                  aria-selected={tab.id === active.id}
                >
                  {tab.label}
                </button>
              ))}
            </div>
          )}
          <CopyCode text={active.code} />
        </>
      }
      foot={active.footnote}
    >
      <div
        className={styles.body}
        id={`${id}-panel`}
        role={tabs.length > 1 ? 'tabpanel' : 'region'}
        aria-labelledby={tabs.length > 1 ? `${id}-tab-${active.id}` : undefined}
        aria-label={tabs.length === 1 ? active.filename : undefined}
      >
        <div className={styles.filename}>{active.filename}</div>
        <CodeSnippet code={active.code} language={active.language} regionLabel={active.filename} scroll />
      </div>
    </Frame>
  );
}
