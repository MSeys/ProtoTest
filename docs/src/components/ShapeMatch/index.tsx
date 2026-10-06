import type {CSSProperties, ReactNode} from 'react';
import Frame from '@site/src/components/Frame';
import styles from './styles.module.css';

type Verdict = 'matched' | 'mismatch' | 'ignored' | 'plain';

export interface ShapeLine {
  /** One line of the actual JSON, as the response printed it. */
  text: string;
  /** Nesting depth, for the indent. */
  depth?: number;
  /** What the matcher did with this line. Lines without a verdict are punctuation. */
  verdict?: Verdict;
  /** Why: the constraint that held, the mismatch the matcher reported, or why it was not compared. */
  note?: ReactNode;
}

interface ShapeMatchProps {
  /** What the JSON is, above it: the request or the subject. */
  subject: ReactNode;
  lines: ShapeLine[];
  caption?: ReactNode;
}

const marks: Record<Verdict, string> = {matched: '✓', mismatch: '✗', ignored: '–', plain: ''};
const words: Record<Verdict, string> = {matched: 'matched', mismatch: 'mismatch', ignored: 'not in the shape', plain: ''};

/*
 * The actual JSON a shape was matched against, line by line, with the matcher's verdict on each: matched, a
 * mismatch with its reason, or not in the shape (objects are partial). The verdicts come from a real matcher run;
 * the figure only lays them out. Wide, the note sits beside its line; narrow, under it.
 */
export default function ShapeMatch({subject, lines, caption}: ShapeMatchProps): ReactNode {
  return (
    <Frame head={<span className={styles.subject}>{subject}</span>} foot={caption}>
      <ol className={styles.lines}>
        {lines.map((line, index) => {
          const verdict = line.verdict ?? 'plain';
          return (
            <li key={index} className={`${styles.line} ${styles[verdict]}`} style={{'--depth': line.depth ?? 0} as CSSProperties}>
              <span className={styles.mark} aria-hidden="true">
                {marks[verdict]}
              </span>
              <code className={styles.json}>{line.text}</code>
              {verdict !== 'plain' && (
                <span className={styles.note}>
                  <span className={styles.word}>{words[verdict]}</span>
                  {line.note && <> · {line.note}</>}
                </span>
              )}
            </li>
          );
        })}
      </ol>
    </Frame>
  );
}
