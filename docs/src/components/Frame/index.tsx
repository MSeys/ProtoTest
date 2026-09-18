import type {ReactNode} from 'react';

import styles from './styles.module.css';

interface FrameProps {
  /** The panel's head: a title, tabs, or both. */
  head?: ReactNode;
  /** A quiet line under the body: a footnote, a source, a hint. */
  foot?: ReactNode;
  children: ReactNode;
  className?: string;
}

/**
 * The frame every home-page visual sits in: the viewer's Panel on the blueprint surface. It opts into the
 * blueprint with data-surface, so the product screens it holds look like ProtoTest does, whichever theme the
 * page is in, and everything inside can use the ordinary tokens.
 */
export default function Frame({head, foot, children, className}: FrameProps): ReactNode {
  return (
    <div data-surface="blueprint" className={`${styles.frame} ${className ?? ''}`}>
      {head && <div className={styles.head}>{head}</div>}
      {children}
      {foot && <div className={styles.foot}>{foot}</div>}
    </div>
  );
}
