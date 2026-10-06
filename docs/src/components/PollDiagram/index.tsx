import type {ReactNode} from 'react';
import {useBaseUrlUtils} from '@docusaurus/useBaseUrl';
import Frame from '@site/src/components/Frame';
import styles from './styles.module.css';

export interface PollRun {
  label: ReactNode;
  /** When each probe started, in ms from the first. Every probe before the last saw the old state. */
  probes: number[];
  /** When the wait ended, in ms from the first probe: the end of the last probe, or the elapsed time a timeout reports. */
  endMs: number;
  /** Whether the last probe saw the state (the test went on) or the deadline passed first. */
  held: boolean;
  /** What the run ended with: the outcome, or the failure message. */
  outcome: ReactNode;
  /** The published trace the values come from. */
  trace?: string;
}

interface PollDiagramProps {
  runs: PollRun[];
  deadlineMs: number;
  caption: ReactNode;
}

/*
 * Bounded polls on one time axis, from measured traces: each probe is a tick, the wait ends where the
 * track turns faint, and the deadline is a line every run shares. The point is the gap between the two:
 * a deadline is a ceiling, and a wait that holds early never pays for it. Static, so nothing moves while
 * it is read; the run text says the same thing for a reader who cannot see the ticks.
 */
export default function PollDiagram({runs, deadlineMs, caption}: PollDiagramProps): ReactNode {
  const {withBaseUrl} = useBaseUrlUtils();
  const scale = Math.max(deadlineMs, ...runs.map((run) => run.endMs)) * 1.02;
  const x = (ms: number) => `${(ms / scale) * 100}%`;
  const seconds = Array.from({length: Math.floor(deadlineMs / 1000) + 1}, (_, s) => s * 1000);

  return (
    <Frame foot={caption}>
      <div className={styles.body}>
        <ol className={styles.runs}>
          {runs.map((run, index) => {
            const probes = run.probes.length === 1 ? '1 probe' : `${run.probes.length} probes`;
            return (
              <li key={index} className={`${styles.run} ${run.held ? styles.held : styles.timedOut}`}>
                <div className={styles.head}>
                  <span className={styles.label}>{run.label}</span>
                  <span className={styles.meta}>
                    {probes} · {run.held ? 'held' : 'timed out'} at {Math.round(run.endMs)} ms
                    {run.trace && (
                      <>
                        {' · '}
                        <a href={withBaseUrl(`/lessons/${run.trace}.prototrace`)}>{run.trace}</a>
                      </>
                    )}
                  </span>
                </div>
                <div className={styles.track} aria-hidden="true">
                  <span className={styles.waited} style={{width: x(run.endMs)}} />
                  <span className={styles.deadline} style={{left: x(deadlineMs)}} />
                  {run.probes.map((at, probe) => {
                    const last = probe === run.probes.length - 1;
                    const tone = last ? (run.held ? styles.hit : styles.miss) : '';
                    return <span key={probe} className={`${styles.probe} ${tone}`} style={{left: x(at)}} />;
                  })}
                </div>
                <span className={styles.outcome}>{run.outcome}</span>
              </li>
            );
          })}
        </ol>
        <div className={styles.axis} aria-hidden="true">
          {seconds.map((ms) => (
            <span key={ms} className={styles.second} style={{left: x(ms)}}>
              {ms === deadlineMs ? `${ms / 1000} s deadline` : `${ms / 1000} s`}
            </span>
          ))}
        </div>
      </div>
    </Frame>
  );
}
