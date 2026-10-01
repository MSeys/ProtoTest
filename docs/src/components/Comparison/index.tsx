import {useId, useState, type ReactNode} from 'react';

import CodeSnippet from '@site/src/components/CodeSnippet';
import Frame from '@site/src/components/Frame';
import tabStyles from '@site/src/components/TabbedCode/styles.module.css';
import CodePane, {type ComparisonFold} from './CodePane';
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

export interface ComparisonSlice {
  /** A file of the same side. */
  file: string;
  /** 1-based inclusive line ranges. */
  ranges: [number, number][];
}

/** One task both sides must do, and where each side does it. */
export interface ComparisonConcern {
  id: string;
  task: string;
  without: {summary: string; slices: ComparisonSlice[]};
  with: {summary: string; home: string; slices: ComparisonSlice[]};
}

interface ComparisonProps {
  without: ComparisonFile[];
  with: ComparisonFile[];
  /** The same comparison task by task, as the table under the code. */
  concerns: ComparisonConcern[];
  withoutLabel?: string;
  withLabel?: string;
}

/** Non-blank lines: blank lines never count, on either side or in either total. */
function meaningful(code: string): number {
  return code.trim().split('\n').filter((line) => line.trim().length > 0).length;
}

function suiteLines(files: ComparisonFile[]): number {
  return files.filter((file) => file.scope === 'suite').reduce((sum, file) => sum + meaningful(file.code), 0);
}

/**
 * The same test written twice, as two tabs over one code surface with the plumbing lines marked, a table of where
 * each task went, and the totals in one sentence. The helper files stay one fold away.
 */
export default function Comparison({
  without,
  with: withProto,
  concerns,
  withoutLabel = 'Without ProtoTest',
  withLabel = 'With ProtoTest',
}: ComparisonProps): ReactNode {
  const id = useId();
  const sides = [
    {id: 'without', label: withoutLabel, files: without},
    {id: 'with', label: withLabel, files: withProto},
  ];
  const [active, setActive] = useState(sides[1].id);
  const [openFolds, setOpenFolds] = useState<Set<string>>(new Set());
  const side = sides.find((candidate) => candidate.id === active)!;
  const test = side.files.find((file) => file.scope === 'test')!;
  const plumbing = (files: ComparisonFile[]) => files.find((file) => file.scope === 'test')!.infrastructureLines.length;

  function toggleFold(key: string) {
    setOpenFolds((current) => {
      const next = new Set(current);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }

  return (
    <div className={styles.comparison}>
      <Frame
        kind="code"
        head={
          <div className={tabStyles.tabs} role="tablist" aria-label="The same test, both ways">
            {sides.map((candidate) => (
              <button
                key={candidate.id}
                type="button"
                role="tab"
                id={`${id}-tab-${candidate.id}`}
                aria-controls={`${id}-panel`}
                aria-selected={candidate.id === active}
                tabIndex={candidate.id === active ? 0 : -1}
                className={`${tabStyles.tab} ${candidate.id === active ? tabStyles.tabActive : ''}`}
                onClick={() => setActive(candidate.id)}>
                {candidate.label}
              </button>
            ))}
          </div>
        }
        foot={
          <>
            <span className={styles.mark} aria-hidden="true" /> {plumbing(side.files)} of {meaningful(test.code)} lines
            are plumbing: they stand the test up instead of describing the scenario.
          </>
        }>
        <div
          className={tabStyles.body}
          id={`${id}-panel`}
          role="tabpanel"
          aria-labelledby={`${id}-tab-${active}`}>
          <div className={tabStyles.filename}>{test.filename}</div>
          <div data-surface="blueprint" className={styles.code}>
            <CodePane
              code={test.code}
              plumbingLines={test.infrastructureLines}
              folds={test.folds ?? []}
              resolve={(fold) => {
                const source = side.files.find((file) => file.filename === fold.source);
                return source ? source.code.trim().split('\n').slice(fold.from - 1, fold.to).join('\n') : null;
              }}
              openKeys={openFolds}
              onToggle={toggleFold}
              keyOf={(fold) => `${side.id}:${test.filename}:${fold.line}`}
            />
          </div>
        </div>
      </Frame>

      <p>
        Per fixture, {plumbing(without)} lines of plumbing without ProtoTest and {plumbing(withProto)} with it. The
        scenario lines stay the same. Both sides also have files written once for the suite:{' '}
        {suiteLines(without)} lines of helpers and DTOs without, {suiteLines(withProto)} with, which also produce the
        trace, the contract coverage and the report.
      </p>

      <div className={styles.tableWrap}>
        <table>
          <thead>
            <tr>
              <th>Task</th>
              <th>{withoutLabel}</th>
              <th>{withLabel}</th>
            </tr>
          </thead>
          <tbody>
            {concerns.map((concern) => (
              <tr key={concern.id}>
                <td>{concern.task}</td>
                <td>{concern.without.summary}</td>
                <td>{concern.with.summary}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <details className={styles.files}>
        <summary>The files written once</summary>
        {sides.map((candidate) =>
          candidate.files
            .filter((file) => file.scope === 'suite')
            .map((file) => (
              <div key={`${candidate.id}:${file.filename}`} className={styles.file}>
                <p className={styles.fileName}>
                  {candidate.label}: <code>{file.filename}</code>, {meaningful(file.code)} lines
                </p>
                <CodeSnippet code={file.code.trim()} language="csharp" regionLabel={file.filename} scroll />
              </div>
            )),
        )}
      </details>
    </div>
  );
}
