import type {ReactNode} from 'react';
import {Highlight} from 'prism-react-renderer';

import prototestPrism from '@site/src/prism/prototest';
import styles from './styles.module.css';

interface CodeSnippetProps {
  code: string;
  language?: string;
  /** 1-based line numbers that are plumbing rather than scenario. */
  plumbingLines?: number[];
  /** 1-based line numbers that carry a numbered callout; the number is the position in this list. */
  callouts?: number[];
  showLineNumbers?: boolean;
  /** The number to give the first line, for rendering a slice of a longer file. */
  startLine?: number;
  /** Keep lines whole and scroll sideways instead of wrapping: for code a reader compares line by line. */
  scroll?: boolean;
  /** A label that makes the code a focusable region, so a keyboard can reach and scroll it. */
  regionLabel?: string;
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
  callouts = [],
  showLineNumbers = false,
  startLine = 1,
  scroll = false,
  regionLabel,
}: CodeSnippetProps): ReactNode {
  const plumbing = new Set(plumbingLines);

  return (
    <Highlight theme={prototestPrism} code={code.trim()} language={language}>
      {({className, tokens, getTokenProps}) => (
        <pre
          className={`${className} ${styles.pre} ${scroll ? styles.scroll : ''}`}
          tabIndex={regionLabel ? 0 : undefined}
          role={regionLabel ? 'region' : undefined}
          aria-label={regionLabel}>
          {tokens.map((line, i) => {
            const isPlumbing = plumbing.has(i + 1);
            const callout = callouts.indexOf(i + 1);
            return (
              <div
                key={i}
                className={`${styles.line} ${isPlumbing ? styles.plumbing : ''} ${callout >= 0 ? styles.annotated : ''}`}>
                {showLineNumbers && <span className={styles.lineNo}>{startLine + i}</span>}
                <span className={styles.lineContent}>
                  {line.map((token, key) => (
                    <span key={key} {...getTokenProps({token})} />
                  ))}
                </span>
                {callout >= 0 && (
                  <span className={styles.callout} aria-hidden="true">{callout + 1}</span>
                )}
              </div>
            );
          })}
        </pre>
      )}
    </Highlight>
  );
}
