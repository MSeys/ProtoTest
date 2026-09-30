import {useId, useState, type KeyboardEvent, type ReactNode} from 'react';
import Link from '@docusaurus/Link';

import Frame from '@site/src/components/Frame';
import CheckView from './CheckView';
import RunView from './RunView';
import StepsView from './StepsView';
import TestViews from './TestViews';
import styles from './styles.module.css';

/*
 * Selected views of test 12 and its run, using values from the committed demo archive.
 */

type ViewId = 'run' | 'steps' | 'timeline' | 'state' | 'evidence' | 'check';

const views: {id: ViewId; label: string}[] = [
  {id: 'run', label: 'Run'},
  {id: 'steps', label: 'Steps'},
  {id: 'timeline', label: 'Timeline'},
  {id: 'state', label: 'State'},
  {id: 'evidence', label: 'Evidence'},
  {id: 'check', label: 'Check'},
];

export default function ViewerWalkthrough(): ReactNode {
  const [view, setView] = useState<ViewId>('run');
  const baseId = useId();

  const move = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key !== 'ArrowRight' && event.key !== 'ArrowLeft' && event.key !== 'Home' && event.key !== 'End') return;
    event.preventDefault();
    const index = views.findIndex((item) => item.id === view);
    const next =
      event.key === 'ArrowRight' ? views[(index + 1) % views.length]
      : event.key === 'ArrowLeft' ? views[(index + views.length - 1) % views.length]
      : event.key === 'Home' ? views[0]
      : views[views.length - 1];
    if (next) {
      setView(next.id);
      event.currentTarget.querySelectorAll<HTMLButtonElement>('[role="tab"]')[views.indexOf(next)]?.focus();
    }
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
          <Link href="https://trace.prototest.dev/?demo=1">Open this run in the viewer</Link>{' '}
          Selected excerpts from <span className={styles.archive}>prototest-demo.prototrace</span>.
        </>
      }>
      <p className={styles.pointer}>
        4 failed, 1 partial. Start with test 08, then 10.
        <button type="button" className={styles.jump} onClick={() => setView('steps')}>
          See test 12&rsquo;s Steps
        </button>
        <button type="button" className={styles.jump} onClick={() => setView('check')}>
          See the failing check
        </button>
      </p>
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
        {view === 'steps' && <StepsView />}
        {(view === 'timeline' || view === 'state' || view === 'evidence') && <TestViews view={view} />}
        {view === 'check' && <CheckView />}
      </div>
    </Frame>
  );
}
