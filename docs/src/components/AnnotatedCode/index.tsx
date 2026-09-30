import type {ReactNode} from 'react';

import CodeSnippet from '@site/src/components/CodeSnippet';
import CopyCode from '@site/src/components/CopyCode';
import Frame from '@site/src/components/Frame';
import styles from './styles.module.css';

export interface CodeCallout {
  /** 1-based line number the callout points at. */
  line: number;
  title: string;
  note: string;
}

interface AnnotatedCodeProps {
  filename: string;
  code: string;
  callouts: CodeCallout[];
  /** A quiet line under the panel: where the snippet comes from, or what to read next. */
  foot?: ReactNode;
}

/*
 * A snippet with numbered notes. The number sits on the line and repeats beside the snippet, so
 * "which part is this about" is answered twice, once where the reader looks and once where they read.
 */
export default function AnnotatedCode({filename, code, callouts, foot}: AnnotatedCodeProps): ReactNode {
  return (
    <Frame
      kind="figure"
      head={
        <>
          <span className={styles.head}>
            <strong>{filename}</strong>
            <small>
              {callouts.length} {callouts.length === 1 ? 'note' : 'notes'}
            </small>
          </span>
          <CopyCode text={code} />
        </>
      }
      foot={foot}
    >
      <CodeSnippet
        code={code}
        showLineNumbers
        callouts={callouts.map((callout) => callout.line)}
        regionLabel={`${filename} code`}
      />
      <ol className={styles.notes}>
        {callouts.map((callout, index) => (
          <li key={callout.line}>
            <span className={styles.n} aria-hidden="true">
              {index + 1}
            </span>
            <div>
              <strong>{callout.title}</strong>
              <p>{callout.note}</p>
            </div>
          </li>
        ))}
      </ol>
    </Frame>
  );
}
