import {Fragment, type ReactNode} from 'react';
import {Highlight} from 'prism-react-renderer';

import CodeSnippet from '@site/src/components/CodeSnippet';
import codeStyles from '@site/src/components/CodeSnippet/styles.module.css';
import prototestPrism from '@site/src/prism/prototest';
import styles from './CodePane.module.css';

export interface ComparisonFold {
  /** 1-based line in this file where the annotation hangs. */
  line: number;
  /** The thing being folded, used as the control's accessible name. */
  label: string;
  /** The file that implements it. */
  source: string;
  /** 1-based inclusive range of that file to reveal. */
  from: number;
  to: number;
  /** One line on what it does. */
  summary: string;
  /** How the code is reached: composed per test, or called from every fixture. */
  reuse: string;
}

interface CodePaneProps {
  code: string;
  plumbingLines: number[];
  folds: ComparisonFold[];
  /** The source lines for a fold, or null when the file cannot be found. */
  resolve: (fold: ComparisonFold) => string | null;
  openKeys: Set<string>;
  onToggle: (key: string) => void;
  keyOf: (fold: ComparisonFold) => string;
}

/**
 * One side's test file, with its capabilities hanging off the lines that declare them. The outer surface is a
 * div rather than a pre, so a revealed implementation can sit inline as a real code block.
 */
export default function CodePane({
  code,
  plumbingLines,
  folds,
  resolve,
  openKeys,
  onToggle,
  keyOf,
}: CodePaneProps): ReactNode {
  const plumbing = new Set(plumbingLines);
  const byLine = new Map<number, ComparisonFold[]>();
  for (const fold of folds) {
    byLine.set(fold.line, [...(byLine.get(fold.line) ?? []), fold]);
  }

  return (
    <Highlight theme={prototestPrism} code={code.trim()} language="csharp">
      {({className, tokens, getTokenProps}) => (
        <div className={`${className} ${codeStyles.pre}`}>
          {tokens.map((line, i) => {
            const number = i + 1;
            const isPlumbing = plumbing.has(number);
            return (
              <Fragment key={i}>
                <div className={`${codeStyles.line} ${isPlumbing ? codeStyles.plumbing : ''}`}>
                  <span className={codeStyles.lineNo}>{number}</span>
                  <span className={codeStyles.lineContent}>
                    {line.map((token, key) => (
                      <span key={key} {...getTokenProps({token})} />
                    ))}
                  </span>
                </div>
                {(byLine.get(number) ?? []).map((fold) => {
                  const key = keyOf(fold);
                  const open = openKeys.has(key);
                  const source = open ? resolve(fold) : null;
                  return (
                    <div key={key} className={styles.fold}>
                      <button
                        type="button"
                        className={styles.foldHead}
                        aria-expanded={open}
                        aria-label={`${open ? 'Hide' : 'Show'} ${fold.label} from ${fold.source}`}
                        onClick={() => onToggle(key)}>
                        <span className={styles.foldToggle} aria-hidden="true">
                          <svg viewBox="0 0 10 10" width="10" height="10">
                            <path
                              d="M1.6 5H8.4"
                              fill="none"
                              stroke="currentColor"
                              strokeWidth="1.5"
                              strokeLinecap="round"
                            />
                            <path
                              className={styles.foldStem}
                              d="M5 1.6V8.4"
                              fill="none"
                              stroke="currentColor"
                              strokeWidth="1.5"
                              strokeLinecap="round"
                            />
                          </svg>
                        </span>
                        <span className={styles.foldSource}>{fold.source}</span>
                        <span className={styles.foldReuse}>{fold.reuse}</span>
                        <span className={styles.foldSummary}>{fold.summary}</span>
                      </button>
                      {open && source && (
                        <div className={styles.foldBody}>
                          <div className={styles.foldBodyHead}>
                            {fold.source} · lines {fold.from}–{fold.to}
                          </div>
                          <CodeSnippet code={source} startLine={fold.from} showLineNumbers />
                        </div>
                      )}
                    </div>
                  );
                })}
              </Fragment>
            );
          })}
        </div>
      )}
    </Highlight>
  );
}
