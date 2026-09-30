import Link from '@docusaurus/Link';
import type {ReactNode} from 'react';

import styles from './styles.module.css';

interface Props {
  demo: string;
  title: string;
  path: string;
}

/*
 * A pointer to a recorded test, drawn as the viewer draws a passing test row: its outcome, its name, what it
 * did, and the way in. The reader recognises the row when the viewer opens.
 */
export default function TraceExample({demo, title, path}: Props): ReactNode {
  const href = `https://trace.prototest.dev/?demo=${encodeURIComponent(demo)}`;
  return (
    <aside className={styles.example} data-surface="blueprint" aria-label={`Recorded test: ${title}`}>
      <div className={styles.row}>
        <span className={styles.status} aria-hidden="true" />
        <span className={styles.text}>
          <strong>{title}</strong>
          <code>{path}</code>
        </span>
        <Link className={styles.open} href={href}>
          Open in the viewer <span aria-hidden="true">↗</span>
        </Link>
      </div>
    </aside>
  );
}
