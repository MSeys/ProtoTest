import Link from '@docusaurus/Link';
import Heading from '@theme/Heading';
import type {ReactNode} from 'react';
import Checkpoint, {type CheckpointContent} from '@site/src/components/Checkpoint';
import sections from '@site/src/data/lessonSections.json';
import styles from './styles.module.css';
export interface LearnNext {
  label: string;
  to: string;
  note: string;
}
interface LearnShellProps {
  level: string;
  minutes: string;
  outcome: string[];
  before: ReactNode[];
  situation: ReactNode;
  checkpoint: CheckpointContent;
  learned: string[];
  next: LearnNext[];
  children: ReactNode;
}
export default function LearnShell({
  level,
  minutes,
  outcome,
  before,
  situation,
  checkpoint,
  learned,
  next,
  children,
}: LearnShellProps): ReactNode {
  return (
    <div className={styles.shell}>
      <header className={styles.head}>
        <span>{level}</span>
        <span>{minutes}</span>
      </header>
      <div className={styles.duo}>
        <section aria-label="Lesson outcomes">
          <strong>By the end</strong>
          <ul>
            {outcome.map((item) => (
              <li key={item}>{item}</li>
            ))}
          </ul>
        </section>
        <section aria-label="Prerequisites">
          <strong>Before you start</strong>
          <ul>
            {before.map((item, index) => (
              <li key={index}>{item}</li>
            ))}
          </ul>
        </section>
      </div>
      <Heading as="h2" id={sections[0].id}>
        {sections[0].value}
      </Heading>
      <div className={styles.body}>{situation}</div>
      <div className={styles.body}>{children}</div>
      <Heading as="h2" id={sections[1].id}>
        {sections[1].value}
      </Heading>
      <Checkpoint {...checkpoint} />
      <Heading as="h2" id={sections[2].id}>
        {sections[2].value}
      </Heading>
      <ul className={styles.learned}>
        {learned.map((item) => (
          <li key={item}>{item}</li>
        ))}
      </ul>
      <Heading as="h2" id={sections[3].id}>
        {sections[3].value}
      </Heading>
      <nav className={styles.next} aria-label="Related lessons and reference">
        {next.map((item) => (
          <Link key={item.to} to={item.to}>
            <strong>{item.label} →</strong>
            <span>{item.note}</span>
          </Link>
        ))}
      </nav>
    </div>
  );
}
