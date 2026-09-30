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
  /** Set by the mobile drawer, which lists every navbar item. */
  mobile?: boolean;
  /** Set by the mobile drawer to close itself after a tap. */
  onClick?: () => void;
}

/**
 * The one external-link control in the header: a brand mark, the name where there is room,
 * and a tooltip for the icon-only state. The mobile drawer gets the labelled row instead.
 */
export default function NavbarMarkLink({
  href,
  name,
  title,
  children,
  mobile,
  onClick,
}: NavbarMarkLinkProps): ReactNode {
  if (mobile) {
    return (
      <li className="menu__list-item">
        <a
          className={`menu__link ${styles.mobile}`}
          href={href}
          target="_blank"
          rel="noopener noreferrer"
          aria-label={title}
          onClick={onClick}
        >
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
      aria-label={title}
      data-tooltip={title}
    >
      {children}
      <span className={styles.label}>{name}</span>
    </a>
  );
}
