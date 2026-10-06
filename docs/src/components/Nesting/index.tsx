import type {ReactNode} from 'react';
import Frame from '@site/src/components/Frame';
import styles from './styles.module.css';

export interface NestingLayer {
  label: ReactNode;
  /** Where the layer sits or what it does on the way in and out. */
  detail?: ReactNode;
}

interface NestingProps {
  /** Outermost first. */
  layers: NestingLayer[];
  /** What every layer wraps: the test body, the backend call. */
  core: ReactNode;
  caption?: ReactNode;
}

/*
 * Layers that wrap a core: the way in passes them outside in, the way out inside out. Each layer carries its step
 * number in both directions, so the mirror reads without arrows. It replaces the docs' mermaid chains that drew one
 * long line for a nesting.
 */
export default function Nesting({layers, core, caption}: NestingProps): ReactNode {
  const last = layers.length * 2 + 1;
  const wrap = (index: number): ReactNode => {
    if (index === layers.length) {
      return (
        <div className={styles.core}>
          <span className={styles.steps}>
            <span className={styles.step}>{layers.length + 1}</span>
          </span>
          <span className={styles.label}>{core}</span>
        </div>
      );
    }
    const layer = layers[index];
    return (
      <div className={styles.layer}>
        <div className={styles.head}>
          <span className={styles.steps}>
            <span className={styles.step} title="on the way in">
              {index + 1}
            </span>
            <span className={styles.out} title="on the way out">
              {last - index}
            </span>
          </span>
          <span className={styles.label}>{layer.label}</span>
          {layer.detail && <span className={styles.detail}>{layer.detail}</span>}
        </div>
        {wrap(index + 1)}
      </div>
    );
  };

  return (
    <Frame foot={caption}>
      <div className={styles.body}>
        <p className={styles.key}>
          <span className={styles.step}>1</span> the order on the way in, <span className={styles.out}>{last}</span> on the way
          out
        </p>
        {wrap(0)}
      </div>
    </Frame>
  );
}
