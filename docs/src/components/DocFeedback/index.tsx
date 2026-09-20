import Link from '@docusaurus/Link';
import {useLocation} from '@docusaurus/router';
import type {ReactNode} from 'react';

import styles from './styles.module.css';

export default function DocFeedback(): ReactNode {
  const {pathname} = useLocation();
  const issue = new URL('https://github.com/MSeys/ProtoTest/issues/new');
  issue.searchParams.set('title', `Docs feedback: ${pathname}`);
  issue.searchParams.set(
    'body',
    `### Page\n\nhttps://prototest.dev${pathname}\n\n### What was unclear or missing?\n\n`,
  );
  issue.searchParams.set('labels', 'documentation');

  return (
    <aside className={styles.feedback} aria-label="Documentation feedback">
      <div>
        <strong>Could this page be clearer?</strong>
        <span>Point to the gap; the page link is filled in for you.</span>
      </div>
      <Link className={styles.link} href={issue.toString()}>
        Suggest an improvement
      </Link>
    </aside>
  );
}
