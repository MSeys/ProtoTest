import {useEffect, useState, type ReactNode} from 'react';
import Frame from '@site/src/components/Frame';
import styles from './styles.module.css';

type Tone = 'neutral' | 'success' | 'danger';

export interface RaceEvent {
  label: string;
  /** Where on the lane, from 0 (left) to 1 (right). Spacing shows order only, never time. */
  at: number;
  tone?: Tone;
  /** The order the diagram reveals the events in, across all lanes: 1, 2, 3... */
  step: number;
}

export interface RaceLane {
  label: string;
  events: RaceEvent[];
}

interface RaceDiagramProps {
  lanes: RaceLane[];
  /** What each order leads to; shown after the last event. */
  outcomes: Array<{text: ReactNode; tone: Tone}>;
  /** The sentence under the diagram. It says the diagram illustrates and does not measure. */
  caption: ReactNode;
}

const STEP_MS = 900;

/*
 * Two or more things racing, drawn on lanes: the events appear in order, then the outcomes. It is an
 * illustration of an order, never a measured timeline, and says so. A reader who asks for reduced motion, or a
 * page without JavaScript, sees the finished diagram at once.
 */
export default function RaceDiagram({lanes, outcomes, caption}: RaceDiagramProps): ReactNode {
  const last = Math.max(...lanes.flatMap((lane) => lane.events.map((event) => event.step)));
  const end = last + 1; // the step that shows the outcomes
  const [step, setStep] = useState(end);

  useEffect(() => {
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;
    setStep(0);
    // Two steps of rest on the finished diagram before it starts again.
    const timer = window.setInterval(() => setStep((s) => (s >= end + 2 ? 0 : s + 1)), STEP_MS);
    return () => window.clearInterval(timer);
  }, [end]);

  const shown = (n: number) => step >= n;
  return (
    <Frame foot={caption}>
      <div className={styles.body} role="img" aria-label="An illustration of the order of events, not measured timings.">
        <span className={styles.badge}>Illustration · not a measurement</span>
        <div className={styles.lanes}>
          {lanes.map((lane) => (
            <div key={lane.label} className={styles.lane}>
              <span className={styles.laneLabel}>{lane.label}</span>
              <div className={styles.track}>
                {lane.events.map((event) => (
                  <span
                    key={event.label}
                    className={`${styles.event} ${styles[event.tone ?? 'neutral']} ${shown(event.step) ? styles.on : ''}`}
                    style={{left: `${event.at * 100}%`}}>
                    <span className={styles.dot} />
                    <span className={styles.eventLabel}>{event.label}</span>
                  </span>
                ))}
              </div>
            </div>
          ))}
        </div>
        <div className={`${styles.outcomes} ${shown(end) ? styles.on : ''}`}>
          {outcomes.map((outcome, index) => (
            <span key={index} className={`${styles.outcome} ${styles[outcome.tone]}`}>
              {outcome.text}
            </span>
          ))}
        </div>
      </div>
    </Frame>
  );
}
