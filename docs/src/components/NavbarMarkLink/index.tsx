import type {ReactNode} from 'react';

import styles from './styles.module.css';

interface NavbarMarkLinkProps {
  href: string;
  /** The visible name: shown in the navbar where there is room, and always in the mobile drawer. */
  name: string;
  /** The accessible name and the tooltip text. */
  title: string;
  /** The mark; drawn in currentColor. */
  children: ReactNode;
  /** A number to show beside the mark, e.g. a star count. */
  count?: number;
  /** Set by the mobile drawer, which lists every navbar item. */
  mobile?: boolean;
  /** Set by the mobile drawer to close itself after a tap. */
  onClick?: () => void;
}

/**
 * The one external-link control in the header: a brand mark, the name where there is room, an optional count,
 * and a tooltip for the icon-only state. The mobile drawer gets the labelled row instead.
 */
export default function NavbarMarkLink({
  href,
  name,
  title,
  children,
  count,
  mobile,
  onClick,
}: NavbarMarkLinkProps): ReactNode {
  const label = typeof count === 'number' ? `${title} — ${count} stars` : title;

  if (mobile) {
    return (
      <li className="menu__list-item">
        <a
          className={`menu__link ${styles.mobile}`}
          href={href}
          target="_blank"
          rel="noopener noreferrer"
          aria-label={label}
          onClick={onClick}>
          {children}
          {name}
        </a>
      </li>
    );
  }

  return (
    <a
      className={styles.link}
      href={href}
      target="_blank"
      rel="noopener noreferrer"
      aria-label={label}
      data-tooltip={title}>
      {children}
      <span className={styles.label}>{name}</span>
      {typeof count === 'number' && (
        <span className={styles.count} aria-hidden="true">
          <svg viewBox="0 0 16 16" width="11" height="11" fill="currentColor">
            <path d="M8 1l2.1 4.3 4.7.7-3.4 3.3.8 4.7L8 11.8 3.8 14l.8-4.7L1.2 6l4.7-.7z" />
          </svg>
          {count}
        </span>
      )}
    </a>
  );
}
