import {useState, type ReactNode} from 'react';

import CodeSnippet from '@site/src/components/CodeSnippet';
import type {ComparisonFile} from './index';
import styles from './Concerns.module.css';

export interface ComparisonSlice {
  /** A file of the same side, by its filename. */
  file: string;
  /** 1-based inclusive line ranges into that file. */
  ranges: [number, number][];
}

export interface ComparisonConcern {
  id: string;
  /** What a fixture has to do, in the reader's words. */
  task: string;
  without: {summary: string; slices: ComparisonSlice[]};
  /** `home` says where the work went: the test, an attribute, the suite host. */
  with: {summary: string; home: string; slices: ComparisonSlice[]};
}

interface ConcernsProps {
  concerns: ComparisonConcern[];
  without: ComparisonFile[];
  with: ComparisonFile[];
}

interface Cost {
  /** Non-blank lines in the fixture file: what the next test pays again. */
  fixture: number;
  /** Non-blank lines in files written once, by file. */
  once: {file: string; lines: number}[];
}

function linesOf(files: ComparisonFile[], name: string): string[] {
  return files.find((file) => file.filename === name)?.code.trim().split('\n') ?? [];
}

function cost(files: ComparisonFile[], slices: ComparisonSlice[]): Cost {
  const fixtureFile = files.find((file) => file.scope === 'test')!.filename;
  const result: Cost = {fixture: 0, once: []};
  for (const slice of slices) {
    const lines = linesOf(files, slice.file);
    const count = slice.ranges.reduce(
      (sum, [from, to]) => sum + lines.slice(from - 1, to).filter((line) => line.trim()).length,
      0,
    );
    if (slice.file === fixtureFile) result.fixture += count;
    else result.once.push({file: slice.file, lines: count});
  }
  return result;
}

/** A cut from the middle of a file keeps its shape but not the file's indentation, which only costs width. */
function dedent(lines: string[]): string {
  const indents = lines.filter((line) => line.trim()).map((line) => line.length - line.trimStart().length);
  const shared = indents.length ? Math.min(...indents) : 0;
  return lines.map((line) => line.slice(shared)).join('\n');
}

/** The code behind one side of a row: each file once, its ranges in order, a gap marked where lines are skipped. */
function SliceCode({files, slices}: {files: ComparisonFile[]; slices: ComparisonSlice[]}): ReactNode {
  if (!slices.length) return <p className={styles.nothing}>Nothing to show: this side has no code for it.</p>;
  return (
    <>
      {slices.map((slice) => {
        const lines = linesOf(files, slice.file);
        return (
          <div key={slice.file} className={styles.slice}>
            <span className={styles.sliceFile}>{slice.file}</span>
            {slice.ranges.map(([from, to], index) => (
              <div key={from}>
                {index > 0 && <span className={styles.gap} aria-label="lines skipped" />}
                <CodeSnippet code={dedent(lines.slice(from - 1, to))} showLineNumbers startLine={from} />
              </div>
            ))}
          </div>
        );
      })}
    </>
  );
}

function CostLine({value, home}: {value: Cost; home?: string}): ReactNode {
  // No code at all means the side does not do it, which is not the same as doing it for free.
  if (!value.fixture && !value.once.length) {
    return (
      <span className={styles.cost}>
        <strong className={styles.absent}>not covered</strong>
      </span>
    );
  }
  return (
    <span className={styles.cost}>
      {home && <span className={styles.home}>{home}</span>}
      <strong className={value.fixture ? '' : styles.zero}>
        {value.fixture} {value.fixture === 1 ? 'line' : 'lines'}
      </strong>
      <span> in the fixture</span>
      {value.once.map((part) => (
        <span key={part.file} className={styles.once}>
          + {part.lines} once, in {part.file}
        </span>
      ))}
    </span>
  );
}

/**
 * The comparison task by task. Each row names a thing every fixture has to do and what it costs on each side
 * — lines the next fixture pays again, lines written once — and opens onto the code from both files.
 */
export default function Concerns({concerns, without, with: withProto}: ConcernsProps): ReactNode {
  const [open, setOpen] = useState<Set<string>>(new Set());

  function toggle(id: string) {
    setOpen((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  return (
    <div className={styles.table}>
      <div className={styles.columns} aria-hidden="true">
        <span>Every fixture needs to…</span>
        <span>Without ProtoTest</span>
        <span>With ProtoTest</span>
      </div>
      {concerns.map((concern) => {
        const isOpen = open.has(concern.id);
        return (
          <div key={concern.id} className={`${styles.concern} ${isOpen ? styles.open : ''}`}>
            <button type="button" className={styles.row} aria-expanded={isOpen} onClick={() => toggle(concern.id)}>
              <span className={styles.task}>
                <i className={styles.expand} aria-hidden="true" />
                {concern.task}
              </span>
              <span className={styles.side}>
                <em className={styles.sideLabel}>Without</em>
                <CostLine value={cost(without, concern.without.slices)} />
                <span className={styles.summary}>{concern.without.summary}</span>
              </span>
              <span className={styles.side}>
                <em className={styles.sideLabel}>With ProtoTest</em>
                <CostLine value={cost(withProto, concern.with.slices)} home={concern.with.home} />
                <span className={styles.summary}>{concern.with.summary}</span>
              </span>
            </button>
            {isOpen && (
              <div className={styles.code}>
                <div className={styles.codeSide}>
                  <SliceCode files={without} slices={concern.without.slices} />
                </div>
                <div className={styles.codeSide}>
                  <SliceCode files={withProto} slices={concern.with.slices} />
                </div>
              </div>
            )}
          </div>
        );
      })}
    </div>
  );
}
