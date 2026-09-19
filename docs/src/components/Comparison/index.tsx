import {useState, type ReactNode} from 'react';

import Frame from '@site/src/components/Frame';
import CodePane, {type ComparisonFold} from './CodePane';
import Concerns, {type ComparisonConcern} from './Concerns';
import styles from './styles.module.css';

export interface ComparisonFile {
  filename: string;
  code: string;
  /** 1-based line numbers that are plumbing rather than scenario. */
  infrastructureLines: number[];
  /** 'test' = written again for every fixture, 'suite' = written once. */
  scope: 'test' | 'suite';
  note: string;
  /** Capabilities declared on this file's lines, implemented elsewhere in the same side. */
  folds?: ComparisonFold[];
}

export type {ComparisonConcern};

interface ComparisonProps {
  without: ComparisonFile[];
  with: ComparisonFile[];
  /** The same comparison task by task; the full files stay one click away. */
  concerns: ComparisonConcern[];
}

interface Ledger {
  /** Every line in the file. */
  total: number;
  /** Lines that are not blank — the lines the bar is drawn from. */
  meaningful: number;
  plumbing: number;
  scenario: number;
}

function ledger(file: ComparisonFile): Ledger {
  const lines = file.code.trim().split('\n');
  const meaningful = lines.filter((line) => line.trim().length > 0).length;
  const plumbing = file.infrastructureLines.length;
  return {total: lines.length, meaningful, plumbing, scenario: meaningful - plumbing};
}

function suiteLines(files: ComparisonFile[]): number {
  return files
    .filter((file) => file.scope === 'suite')
    .reduce((sum, file) => sum + file.code.trim().split('\n').length, 0);
}

/** Both sides share one scale, so the with-bar's empty tail is the plumbing you stop writing. */
function Bar({value, max}: {value: Ledger; max: number}): ReactNode {
  return (
    <div className={styles.track} aria-hidden="true">
      <span className={styles.plumbing} style={{width: `${(value.plumbing / max) * 100}%`}} />
      <span className={styles.scenario} style={{width: `${(value.scenario / max) * 100}%`}} />
    </div>
  );
}

export default function Comparison({without, with: withProto, concerns}: ComparisonProps): ReactNode {
  const [showFiles, setShowFiles] = useState(false);
  const [openFolds, setOpenFolds] = useState<Set<string>>(new Set());
  const [openPanes, setOpenPanes] = useState<Set<string>>(new Set());

  const withoutTest = without.find((file) => file.scope === 'test')!;
  const withTest = withProto.find((file) => file.scope === 'test')!;
  const withoutLedger = ledger(withoutTest);
  const withLedger = ledger(withTest);
  const max = Math.max(withoutLedger.meaningful, withLedger.meaningful);

  const groups = [
    {id: 'without', label: 'Without ProtoTest', files: without, ledger: withoutLedger},
    {id: 'with', label: 'With ProtoTest', files: withProto, ledger: withLedger},
  ];

  function toggleFold(side: string, key: string) {
    setOpenFolds((current) => {
      const next = new Set(current);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
    // Opening a capability should reveal it, even when the pane is capped.
    setOpenPanes((current) => new Set(current).add(side));
  }

  function togglePane(side: string) {
    setOpenPanes((current) => {
      const next = new Set(current);
      if (next.has(side)) next.delete(side);
      else next.add(side);
      return next;
    });
  }

  return (
    <Frame
      head={
        <>
          <strong className={styles.title}>The same scenario, written twice.</strong>
          <span className={styles.delta}>{withoutTest.filename}, both ways</span>
        </>
      }
      foot={
        <span className={styles.once}>
          <strong>Written once.</strong> Without, {suiteLines(without)} lines of helpers and DTOs. With,{' '}
          {suiteLines(withProto)} lines — the host, the collectors and the sinks that produce the trace, the
          contract coverage and the report.
        </span>
      }>
      {/* The point of the comparison, stated once and large: the plumbing each new fixture pays for. */}
      <div className={styles.score}>
        {groups.map((group) => (
          <div key={group.id} className={`${styles.side} ${group.id === 'with' ? styles.sideWith : ''}`}>
            <span className={styles.sideLabel}>{group.label}</span>
            <span className={styles.figure}>
              <strong>{group.ledger.plumbing}</strong>
              <span>lines of plumbing</span>
            </span>
            <span className={styles.context}>
              in a {group.ledger.meaningful}-line fixture, {group.ledger.scenario} of them the scenario
            </span>
            <Bar value={group.ledger} max={max} />
          </div>
        ))}
      </div>

      <Concerns concerns={concerns} without={without} with={withProto} />

      <button
        type="button"
        className={styles.filesToggle}
        aria-expanded={showFiles}
        onClick={() => setShowFiles((current) => !current)}>
        {showFiles ? 'Hide the full files' : 'Show the full files'}
      </button>

      {showFiles && (
        <div className={styles.preview}>
          {groups.map((group) => {
            const test = group.files.find((file) => file.scope === 'test')!;
            return (
              <div key={group.id} className={styles.doc}>
                <div className={styles.docHead}>
                  <span className={styles.docSide}>{group.label}</span>
                  <span className={styles.docName}>{test.filename}</span>
                  <span className={styles.docScope}>per fixture</span>
                </div>
                <div className={`${styles.docBody} ${openPanes.has(group.id) ? styles.docBodyOpen : ''}`}>
                  <CodePane
                    code={test.code}
                    plumbingLines={test.infrastructureLines}
                    folds={test.folds ?? []}
                    resolve={(fold) => {
                      const source = group.files.find((file) => file.filename === fold.source);
                      if (!source) return null;
                      return source.code.trim().split('\n').slice(fold.from - 1, fold.to).join('\n');
                    }}
                    openKeys={openFolds}
                    onToggle={(key) => toggleFold(group.id, key)}
                    keyOf={(fold) => `${group.id}:${test.filename}:${fold.line}`}
                  />
                  {!openPanes.has(group.id) && <div className={styles.fade} />}
                </div>
                <button
                  type="button"
                  className={styles.docExpand}
                  aria-expanded={openPanes.has(group.id)}
                  onClick={() => togglePane(group.id)}>
                  {openPanes.has(group.id)
                    ? 'Collapse'
                    : `Show all ${test.code.trim().split('\n').length} lines`}
                </button>
              </div>
            );
          })}
        </div>
      )}

      <div className={styles.notes}>
        <p className={styles.contrast}>
          A base class can share the left-hand lines too. The difference is where the work happens: inherited
          setup runs again for every fixture against shared state, while a capability is composed onto a single
          test on a context the framework manages.
        </p>

        <div className={styles.owned}>
          <span className={styles.ownedLabel}>Coordinated lifecycle</span>
          <span className={styles.ownedItem}>Each test gets its own context and data</span>
          <span className={styles.ownedItem}>Cleanup runs deterministically</span>
          <span className={styles.ownedItem}>Tests can run in parallel</span>
        </div>
      </div>
    </Frame>
  );
}
