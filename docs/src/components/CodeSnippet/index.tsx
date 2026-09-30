import type {CSSProperties, ReactNode} from 'react';
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

interface Token {
  types: string[];
  content: string;
  empty?: boolean;
}

/**
 * A wrapped line's leading indentation, taken out of the text and returned as a width. Left in, the spaces
 * are a break opportunity: a long first token would wrap straight after them and leave the line number
 * alone on its row. As padding the indent still reads, and the hanging indent adds to it.
 */
function lift(line: Token[]): {indent: number; tokens: Token[]} {
  let indent = 0;
  const tokens: Token[] = [];
  let leading = true;
  for (const token of line) {
    if (!leading) {
      tokens.push(token);
      continue;
    }
    const space = /^[ \t]*/.exec(token.content)?.[0] ?? '';
    indent += space.replace(/\t/g, '    ').length;
    const rest = token.content.slice(space.length);
    if (rest) {
      leading = false;
      tokens.push({...token, content: rest});
    }
  }
  return {indent, tokens};
}

/**
 * Code for the home-page panels, on the sunken code surface with the same token-based theme as every other
 * code block on the site. Long lines wrap with a hanging indent instead of scrolling sideways, so a phone
 * shows the whole line.
 *
 * Plumbing lines keep their line number in the normal quiet tone and are marked with a dashed gutter rule.
 * Coloured numbers would read as syntax, and fading the number past legibility hides the count it exists for.
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
          data-surface="blueprint"
          className={`${className} ${styles.pre} ${scroll ? styles.scroll : ''}`}
          tabIndex={regionLabel ? 0 : undefined}
          role={regionLabel ? 'region' : undefined}
          aria-label={regionLabel}>
          {tokens.map((raw, i) => {
            const isPlumbing = plumbing.has(i + 1);
            const callout = callouts.indexOf(i + 1);
            const {indent, tokens: line} = scroll ? {indent: 0, tokens: raw} : lift(raw);
            return (
              <div
                key={i}
                className={`${styles.line} ${isPlumbing ? styles.plumbing : ''} ${callout >= 0 ? styles.annotated : ''}`}>
                {showLineNumbers && <span className={styles.lineNo}>{startLine + i}</span>}
                <span className={styles.lineContent} style={indent ? ({'--indent': indent} as CSSProperties) : undefined}>
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
