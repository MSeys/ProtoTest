import type {ReactNode} from 'react';
import Link from '@docusaurus/Link';
import {useDocsSidebar} from '@docusaurus/plugin-content-docs/client';
import type {PropSidebarItem} from '@docusaurus/plugin-content-docs';

import styles from './styles.module.css';

/*
 * The Learn tracks as cards, read from the Learn sidebar: each track's folder holds its label, its order and what
 * it teaches (customProps in _category_.json), and its lessons are the pages inside it. A lesson added to a
 * folder shows up here without editing this page.
 */

interface TrackProps {
  outcome?: string;
  time?: string;
  needs?: string;
}

function lessons(items: PropSidebarItem[]): {label: string; href: string}[] {
  return items.flatMap((item) => {
    if (item.type === 'link') return [{label: item.label, href: item.href}];
    if (item.type === 'category') return lessons(item.items);
    return [];
  });
}

export default function LearnTracks(): ReactNode {
  const sidebar = useDocsSidebar();
  const tracks = (sidebar?.items ?? []).filter((item) => item.type === 'category');
  return (
    <ol className={styles.tracks}>
      {tracks.map((track, index) => {
        if (track.type !== 'category') return null;
        const props = (track.customProps ?? {}) as TrackProps;
        const list = lessons(track.items);
        return (
          <li key={track.label} className={styles.track}>
            <header className={styles.head}>
              <span className={styles.number}>{index + 1}</span>
              <strong>{track.label}</strong>
              {props.time && <small>{props.time}</small>}
            </header>
            {props.outcome && <p className={styles.outcome}>{props.outcome}</p>}
            {list.length > 0 && (
              <ol className={styles.lessons}>
                {list.map((lesson) => (
                  <li key={lesson.href}>
                    <Link to={lesson.href}>{lesson.label}</Link>
                  </li>
                ))}
              </ol>
            )}
            {props.needs && <p className={styles.needs}>Needs: {props.needs}</p>}
          </li>
        );
      })}
    </ol>
  );
}
