import {useState, type ReactNode} from 'react';

import CodeSnippet from '@site/src/components/CodeSnippet';
import styles from './styles.module.css';

export interface ComparisonFile {
  filename: string;
  code: string;
  /** 1-based line numbers that are plumbing rather than scenario. */
  infrastructureLines: number[];
  /** 'test' = written again for every fixture, 'suite' = written once. */
  scope: 'test' | 'suite';
  note: string;
}

interface ComparisonProps {
  without: ComparisonFile[];
  with: ComparisonFile[];
}

export default function Comparison({without, with: withProto}: ComparisonProps): ReactNode {
  const [showProto, setShowProto] = useState(false);
  const [fileIndex, setFileIndex] = useState(0);
  const [expanded, setExpanded] = useState(false);

  const files = showProto ? withProto : without;
  const active = files[Math.min(fileIndex, files.length - 1)];

  const lines = active.code.trim().split('\n');
  const total = lines.length;
  const meaningful = lines.filter((line) => line.trim().length > 0).length;
  const infrastructure = active.infrastructureLines.length;
  const behaviour = meaningful - infrastructure;
  const behaviourShare = Math.round((behaviour / meaningful) * 100);

  const suiteTotal = files
    .filter((file) => file.scope === 'suite')
    .reduce((sum, file) => sum + file.code.trim().split('\n').length, 0);
  const perFixturePlumbing = files
    .filter((file) => file.scope === 'test')
    .reduce((sum, file) => sum + file.infrastructureLines.length, 0);

  function selectSide(proto: boolean) {
    setShowProto(proto);
    setFileIndex(0);
    setExpanded(false);
  }

  return (
    <div className={styles.frame}>
      <div className={styles.toolbar}>
        <div className={styles.tabs}>
          <button
            type="button"
            className={`${styles.tab} ${!showProto ? styles.tabActive : ''}`}
            onClick={() => selectSide(false)}
            aria-pressed={!showProto}>
            Without ProtoTest
          </button>
          <button
            type="button"
            className={`${styles.tab} ${showProto ? styles.tabActive : ''}`}
            onClick={() => selectSide(true)}
            aria-pressed={showProto}>
            With ProtoTest
          </button>
        </div>
        <div className={styles.suiteTotal}>
          {perFixturePlumbing} lines of plumbing per fixture · {suiteTotal} written once
        </div>
      </div>

      <div className={styles.files}>
        {files.map((file, index) => (
          <button
            key={file.filename}
            type="button"
            className={`${styles.file} ${index === fileIndex ? styles.fileActive : ''}`}
            onClick={() => {
              setFileIndex(index);
              setExpanded(false);
            }}
            aria-pressed={index === fileIndex}>
            {file.filename}
            <span
              className={`${styles.scope} ${
                file.scope === 'test' ? styles.scopeTest : styles.scopeSuite
              }`}>
              {file.scope === 'test' ? 'per fixture' : 'written once'}
            </span>
          </button>
        ))}
      </div>

      <div className={styles.note}>{active.note}</div>

      <div className={styles.stats}>
        <span className={styles.statTotal}>
          <strong>{total}</strong> lines
        </span>
        <span className={styles.statSplit}>
          <i className={styles.swatchBehaviour} />
          <strong>{behaviourShare}%</strong> describes the scenario
        </span>
        <span className={styles.statSplit}>
          <i className={styles.swatchInfra} />
          <strong>{infrastructure}</strong> lines of plumbing
        </span>
      </div>

      <div className={`${styles.codeArea} ${expanded ? styles.codeAreaExpanded : ''}`}>
        <CodeSnippet
          code={active.code}
          dimLines={active.infrastructureLines}
          showLineNumbers
        />
        {!expanded && <div className={styles.fade} />}
      </div>

      <button type="button" className={styles.expand} onClick={() => setExpanded((value) => !value)}>
        {expanded ? 'Collapse' : `Show all ${total} lines`}
      </button>
    </div>
  );
}
