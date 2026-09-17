import {useState, type ReactNode} from 'react';

import CodeSnippet from '@site/src/components/CodeSnippet';
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
    <div className={styles.frame}>
      <div className={styles.tabs}>
        {tabs.map((tab) => (
          <button
            key={tab.id}
            type="button"
            className={`${styles.tab} ${tab.id === activeId ? styles.tabActive : ''}`}
            onClick={() => setActiveId(tab.id)}
            aria-pressed={tab.id === activeId}>
            {tab.label}
          </button>
        ))}
      </div>
      <div className={styles.filename}>{active.filename}</div>
      <div className={styles.body}>
        <CodeSnippet code={active.code} />
      </div>
      {active.footnote && <div className={styles.footnote}>{active.footnote}</div>}
    </div>
  );
}
