import type {ReactNode} from 'react';

import styles from './styles.module.css';

interface FrameProps {
  /** The panel's head: a title, tabs, or both. */
  head?: ReactNode;
  /** A quiet line under the body: a footnote, a source, a hint. */
  foot?: ReactNode;
  children: ReactNode;
  className?: string;
  /** Product previews use blueprint; explanatory panels follow the reader's theme. */
  surface?: 'blueprint' | 'page';
}

/**
 * Shared panel chrome. Product previews opt into blueprint; explanatory panels follow the page theme.
 */
export default function Frame({head, foot, children, className, surface = 'blueprint'}: FrameProps): ReactNode {
  return (
    <div data-surface={surface === 'blueprint' ? 'blueprint' : undefined} className={`${styles.frame} ${className ?? ''}`}>
      {head && <div className={styles.head}>{head}</div>}
      {children}
      {foot && <div className={styles.foot}>{foot}</div>}
    </div>
  );
}
