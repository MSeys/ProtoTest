import type {ReactNode} from 'react';
import Frame from '@site/src/components/Frame';
import styles from './styles.module.css';

export interface ContainmentBox {
  label: ReactNode;
  detail?: ReactNode;
  /** What lives inside. Boxes with contents of their own take a full row; the rest share rows as cards. */
  contains?: ContainmentBox[];
  /** The one box the figure is about: drawn in the accent. */
  focus?: boolean;
}

interface ContainmentProps {
  root: ContainmentBox;
  caption?: ReactNode;
}

function Box({box, depth}: {box: ContainmentBox; depth: number}): ReactNode {
  const contents = box.contains ?? [];
  const nested = contents.filter((child) => child.contains?.length);
  const cards = contents.filter((child) => !child.contains?.length);
  return (
    <div className={`${styles.box} ${depth % 2 ? styles.odd : ''} ${box.focus ? styles.focus : ''}`}>
      <div className={styles.head}>
        <span className={styles.label}>{box.label}</span>
        {box.detail && <span className={styles.detail}>{box.detail}</span>}
      </div>
      {cards.length > 0 && (
        <div className={styles.cards}>
          {cards.map((child, index) => (
            <Box key={index} box={child} depth={depth + 1} />
          ))}
        </div>
      )}
      {nested.map((child, index) => (
        <Box key={index} box={child} depth={depth + 1} />
      ))}
    </div>
  );
}

/*
 * What lives inside what: one box per owner, its contents inside it, cards for the leaves. It replaces the docs'
 * mermaid trees that drew "has a" as arrows, which a reader had to fold back into boxes, and which shrank to
 * nothing on a phone.
 */
export default function Containment({root, caption}: ContainmentProps): ReactNode {
  return (
    <Frame foot={caption}>
      <div className={styles.body}>
        <Box box={root} depth={0} />
      </div>
    </Frame>
  );
}
