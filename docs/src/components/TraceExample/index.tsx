import Link from '@docusaurus/Link';
import type {ReactNode} from 'react';

import styles from './styles.module.css';

interface Props {
  demo: string;
  title: string;
  path: string;
}

export default function TraceExample({demo, title, path}: Props): ReactNode {
  const href = `https://trace.prototest.dev/?demo=${encodeURIComponent(demo)}`;
  return (
    <aside className={styles.example} data-surface="blueprint">
      <span className={styles.kind}>SCENARIO → EXECUTION → EVIDENCE</span>
      <div>
        <strong>{title}</strong>
        <code>{path}</code>
      </div>
      <Link className={styles.link} href={href}>Open this test in the trace viewer →</Link>
    </aside>
  );
}
