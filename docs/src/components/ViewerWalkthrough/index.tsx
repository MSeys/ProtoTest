import {useId, useState, type KeyboardEvent, type ReactNode} from 'react';

import Frame from '@site/src/components/Frame';
import CheckView from './CheckView';
import RunView from './RunView';
import StoryView from './StoryView';
import styles from './styles.module.css';

/*
 * The demo trace, drawn the way the viewer draws it, with the three views a failure is read in: the run,
 * the failed test's story, and the failing check's inspector. Every name, value and duration is copied
 * from viewer/public/demos/prototest-demo.prototrace, not invented.
 */

type ViewId = 'run' | 'story' | 'check';

const views: {id: ViewId; label: string}[] = [
  {id: 'run', label: 'Run'},
  {id: 'story', label: 'Story'},
  {id: 'check', label: 'Check'},
];

export default function ViewerWalkthrough(): ReactNode {
  const [view, setView] = useState<ViewId>('run');
  const baseId = useId();

  const move = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key !== 'ArrowRight' && event.key !== 'ArrowLeft') return;
    event.preventDefault();
    const index = views.findIndex((item) => item.id === view);
    const next = views[(index + (event.key === 'ArrowRight' ? 1 : views.length - 1)) % views.length];
    if (next) setView(next.id);
  };

  return (
    <Frame
      head={
        <span className={styles.headTitle}>
          ProtoTrace viewer
          <small>prototest-demo.prototrace</small>
        </span>
      }
      foot={
        <>
          Drawn from <span className={styles.archive}>prototest-demo.prototrace</span>, as the viewer shows it.
        </>
      }>
      <div className={styles.tabs} role="tablist" aria-label="Views of the demo trace" onKeyDown={move}>
        {views.map((item) => (
          <button
            key={item.id}
            type="button"
            role="tab"
            id={`${baseId}-${item.id}`}
            aria-selected={item.id === view}
            aria-controls={`${baseId}-panel`}
            tabIndex={item.id === view ? 0 : -1}
            className={`${styles.tab} ${item.id === view ? styles.tabActive : ''}`}
            onClick={() => setView(item.id)}>
            {item.label}
          </button>
        ))}
      </div>
      <div className={styles.view} id={`${baseId}-panel`} role="tabpanel" aria-labelledby={`${baseId}-${view}`}>
        {view === 'run' && <RunView />}
        {view === 'story' && <StoryView />}
        {view === 'check' && <CheckView />}
      </div>
    </Frame>
  );
}
