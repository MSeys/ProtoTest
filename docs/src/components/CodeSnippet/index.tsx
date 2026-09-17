import type {ReactNode} from 'react';
import {Highlight, themes} from 'prism-react-renderer';

import styles from './styles.module.css';

interface CodeSnippetProps {
  code: string;
  language?: string;
  /** 1-based line numbers to render dimmed, marking them as infrastructure noise. */
  dimLines?: number[];
  showLineNumbers?: boolean;
}

/**
 * Fixed-dark-theme code renderer for panels that always sit on a dark
 * background regardless of the site's own light/dark mode — using
 * @theme/CodeBlock there would pick up the site's prism theme and render
 * dark-on-dark or light-on-light text.
 */
export default function CodeSnippet({
  code,
  language = 'csharp',
  dimLines = [],
  showLineNumbers = false,
}: CodeSnippetProps): ReactNode {
  const dimmed = new Set(dimLines);

  return (
    <Highlight theme={themes.oneDark} code={code.trim()} language={language}>
      {({className, style, tokens, getLineProps, getTokenProps}) => (
        <pre className={`${className} ${styles.pre}`} style={style}>
          {tokens.map((line, i) => {
            const lineProps = getLineProps({line});
            const isDim = dimmed.has(i + 1);
            return (
              <div
                {...lineProps}
                key={i}
                className={`${lineProps.className ?? ''} ${styles.line} ${isDim ? styles.dim : ''}`}>
                {showLineNumbers && <span className={styles.lineNo}>{i + 1}</span>}
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
