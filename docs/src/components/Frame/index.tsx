import type {ReactNode} from 'react';
import styles from './styles.module.css';
interface FrameProps {
  head?: ReactNode;
  foot?: ReactNode;
  children: ReactNode;
  className?: string;
  /** Figures follow the page; code and authentic product previews opt into blueprint. */
  kind?: 'figure' | 'code' | 'preview';
}
export default function Frame({head, foot, children, className, kind = 'figure'}: FrameProps): ReactNode {
  return (
    <div
      data-frame={kind}
      data-surface={kind === 'figure' ? undefined : 'blueprint'}
      className={`${styles.frame} ${className ?? ''}`}
    >
      {head && <div className={styles.head}>{head}</div>}
      {children}
      {foot && <div className={styles.foot}>{foot}</div>}
    </div>
  );
}
