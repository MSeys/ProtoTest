import {useState, type ReactNode} from 'react';

import CodeSnippet from '@site/src/components/CodeSnippet';
import Frame from '@site/src/components/Frame';
import styles from './styles.module.css';

export interface CodeTab {
  id: string;
  label: string;
  filename: string;
  code: string;
  footnote?: string;
}

interface TabbedCodeProps {
  tabs: CodeTab[];
}

/*
 * Snippets behind underline tabs. The tab bar scrolls sideways when there are more tabs than fit, the file name
 * sits on its own bar above the code, and the code keeps its lines whole and scrolls inside itself - so a
 * phone shows the snippet as written instead of rewrapping it.
 */
export default function TabbedCode({tabs}: TabbedCodeProps): ReactNode {
  const [activeId, setActiveId] = useState(tabs[0].id);
  const active = tabs.find((tab) => tab.id === activeId)!;

  return (
    <Frame
      head={
        <div className={styles.tabs} role="tablist">
          {tabs.map((tab) => (
            <button
              key={tab.id}
              type="button"
              role="tab"
              className={`${styles.tab} ${tab.id === activeId ? styles.tabActive : ''}`}
              onClick={() => setActiveId(tab.id)}
              aria-selected={tab.id === activeId}>
              {tab.label}
            </button>
          ))}
        </div>
      }
      foot={active.footnote}>
      <div className={styles.body} role="tabpanel">
        <div className={styles.filename}>{active.filename}</div>
        <CodeSnippet code={active.code} scroll />
      </div>
    </Frame>
  );
}
