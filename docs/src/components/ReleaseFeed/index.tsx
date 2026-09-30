import type {ReactNode} from 'react';
import Link from '@docusaurus/Link';

import {releases} from '@site/src/data/changelog.generated';
import styles from './styles.module.css';

/** One quiet release line, generated from the same changelog as the release page. */

const months = ['jan', 'feb', 'mar', 'apr', 'may', 'jun', 'jul', 'aug', 'sep', 'oct', 'nov', 'dec'];

function formatDate(iso: string): string {
  const [year, month, day] = iso.split('-');
  const monthName = months[Number(month) - 1] ?? month;
  return `${Number(day)} ${monthName} ${year}`;
}

export default function ReleaseFeed(): ReactNode {
  const [latest] = releases;
  if (!latest) return null;
  return <div className={`container ${styles.feed}`} aria-label="Latest release">
    <span>Latest release: <strong>{latest.version}</strong> · {formatDate(latest.date)}</span>
    <Link to="/changelog">Release notes →</Link>
  </div>;
}
