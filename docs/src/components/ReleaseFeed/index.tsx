import type {ReactNode} from 'react';
import Link from '@docusaurus/Link';

import {releases} from '@site/src/data/releases';
import styles from './styles.module.css';

/*
 * The homepage release feed: the latest release with its date and a one-line summary, and the release
 * before it, so a visitor can see the project is moving. The data lives in src/data/releases.ts and is
 * checked against CHANGELOG.md by eng/check-docs.ps1.
 */

const months = ['jan', 'feb', 'mar', 'apr', 'may', 'jun', 'jul', 'aug', 'sep', 'oct', 'nov', 'dec'];

function formatDate(iso: string): string {
  const [year, month, day] = iso.split('-');
  const monthName = months[Number(month) - 1] ?? month;
  return `${Number(day)} ${monthName} ${year}`;
}

export default function ReleaseFeed(): ReactNode {
  const [latest, previous] = releases;

  return (
    <section className={styles.section} aria-label="Latest releases">
      <div className="container">
        <div className={styles.feed}>
          <span className={styles.label}>Latest release</span>
          {latest && (
            <p className={styles.latest}>
              <b>{latest.version}</b>
              <span className={styles.date}>· {formatDate(latest.date)}</span>
              <span className={styles.summary}>{latest.summary}</span>
            </p>
          )}
          {previous && (
            <p className={styles.previous}>
              Before that: <b>{previous.version}</b> on {formatDate(previous.date)}
            </p>
          )}
          <Link className={styles.link} to="/changelog">
            Every release and every breaking change →
          </Link>
        </div>
      </div>
    </section>
  );
}
