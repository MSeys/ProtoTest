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

export default function TabbedCode({tabs}: TabbedCodeProps): ReactNode {
  const [activeId, setActiveId] = useState(tabs[0].id);
  const active = tabs.find((tab) => tab.id === activeId)!;

  return (
    <Frame
      head={
        <>
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
          <span className={styles.filename}>{active.filename}</span>
        </>
      }
      foot={active.footnote}>
      <div className={styles.body}>
        <CodeSnippet code={active.code} />
      </div>
    </Frame>
  );
}
