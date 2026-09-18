import type {ReactNode} from 'react';
import {Highlight} from 'prism-react-renderer';

import prototestPrism from '@site/src/prism/prototest';
import styles from './styles.module.css';

interface CodeSnippetProps {
  code: string;
  language?: string;
  /** 1-based line numbers that are plumbing rather than scenario. */
  plumbingLines?: number[];
  showLineNumbers?: boolean;
  /** The number to give the first line, for rendering a slice of a longer file. */
  startLine?: number;
}

/**
 * Code for the home-page panels, on the sunken code surface with the same token-based theme as every other
 * code block on the site. Long lines wrap with a hanging indent instead of scrolling sideways, so a phone
 * shows the whole line.
 *
 * Plumbing lines keep their line number in the normal quiet tone and are marked with a dashed gutter rule —
 * coloured numbers would read as syntax, and fading the number past legibility hides the count it exists for.
 */
export default function CodeSnippet({
  code,
  language = 'csharp',
  plumbingLines = [],
  showLineNumbers = false,
  startLine = 1,
}: CodeSnippetProps): ReactNode {
  const plumbing = new Set(plumbingLines);

  return (
    <Highlight theme={prototestPrism} code={code.trim()} language={language}>
      {({className, tokens, getTokenProps}) => (
        <pre className={`${className} ${styles.pre}`}>
          {tokens.map((line, i) => {
            const isPlumbing = plumbing.has(i + 1);
            return (
              <div
                key={i}
                className={`${styles.line} ${isPlumbing ? styles.plumbing : ''}`}>
                {showLineNumbers && <span className={styles.lineNo}>{startLine + i}</span>}
                <span className={styles.lineContent}>
                  {line.map((token, key) => (
                    <span key={key} {...getTokenProps({token})} />
                  ))}
                </span>
              </div>
            );
          })}
        </pre>
      )}
    </Highlight>
  );
}
