import {useId, type CSSProperties, type ReactNode} from 'react';

import {
  attention,
  bodySteps,
  callStep,
  check,
  evidence,
  observed,
  run,
  runClock,
  state,
  test,
  timeline,
  visibility,
  type At,
  type Step,
} from './demo';
import styles from './styles.module.css';

/*
 * The ProtoTrace viewer, drawn for the docs. Each part follows a viewer component - the run header, Needs
 * attention, the verdict bar, the phase band, a Steps row, a Timeline row, a State lifeline, an Evidence
 * entry, the details - and reads the demo run's values from demo.ts. A change to the viewer is made here
 * once; every page that shows the viewer uses this component.
 */

export type ViewerScreen =
  | 'run'
  | 'run-timeline'
  | 'run-details'
  | 'visibility'
  | 'steps'
  | 'timeline'
  | 'state'
  | 'evidence'
  | 'check';

const tone = (token: string): CSSProperties => ({'--node-color': `var(${token})`}) as CSSProperties;
const marker = (token: string): CSSProperties => ({'--marker': `var(${token})`}) as CSSProperties;
const place = ([left, width]: At): CSSProperties => ({left: `${left}%`, width: `max(2px, ${width}%)`});
const onRun = (start: number, length: number): At => [(start / run.duration) * 100, (length / run.duration) * 100];

/* ── Shared parts ─────────────────────────────────────────────────────────────────────────── */

function Chevron({open}: {open?: boolean}): ReactNode {
  return <i className={`${styles.chevron} ${open ? styles.chevronOpen : ''}`} aria-hidden="true" />;
}

function Hatch({at, className}: {at: At; className?: string}): ReactNode {
  return <i className={`${styles.hatch} ${className ?? ''}`} style={place(at)} />;
}

function Dot({outcome}: {outcome: string}): ReactNode {
  return <i className={`${styles.dot} ${styles[outcome]}`} aria-hidden="true" />;
}

function Tabs({items, active}: {items: string[]; active: string}): ReactNode {
  return (
    <div className={styles.tabs} aria-hidden="true">
      {items.map((item) => (
        <span key={item} className={item === active ? styles.tabActive : undefined}>
          {item}
        </span>
      ))}
    </div>
  );
}

function Chip({label, count, on, danger}: {label: string; count?: number; on?: boolean; danger?: boolean}): ReactNode {
  return (
    <span className={`${styles.chip} ${on ? styles.chipOn : ''} ${danger ? styles.chipDanger : ''}`}>
      {label}
      {count !== undefined && <b>{count}</b>}
    </span>
  );
}

function Legend({items}: {items: [string, string][]}): ReactNode {
  return (
    <p className={styles.legend}>
      {items.map(([label, token]) => (
        <span key={label} style={marker(token)}>
          {label}
        </span>
      ))}
    </p>
  );
}

/* ── The run ──────────────────────────────────────────────────────────────────────────────── */

function RunHeader(): ReactNode {
  return (
    <header className={styles.runHead}>
      <p className={styles.runVerdict}>
        {run.verdict.map((part, index) => (
          <span key={part.text}>
            <span className={styles[part.tone]}>{part.text}</span>
            {index < run.verdict.length - 1 && <span className={styles.sep}>, </span>}
          </span>
        ))}
      </p>
      <p className={styles.runMeta}>
        {run.meta.map((value) => (
          <span key={value}>{value}</span>
        ))}
        <span className={styles.dim}>{run.file}</span>
      </p>
      <div
        className={styles.strip}
        role="img"
        aria-label="One tick per test: tests 8, 10, 12 and 15 failed, test 11 is partial"
      >
        {run.outcomes.map((outcome, index) => (
          <i key={index} className={styles[`tick-${outcome}`]} />
        ))}
      </div>
    </header>
  );
}

function Attention(): ReactNode {
  return (
    <section className={styles.panel}>
      <p className={styles.panelHead}>Needs attention</p>
      {attention.map((entry) => (
        <div key={`${entry.number}-${entry.title}`} className={`${styles.issue} ${styles[`issue-${entry.outcome}`]}`}>
          <b className={styles.issueNumber}>{entry.number}</b>
          <span className={styles.issueMain}>
            <strong>{entry.title}</strong>
            <span className={styles.issueLine}>
              <span className={styles.issueRule}>{entry.rule}</span>
              <span>{entry.reason}</span>
            </span>
            {entry.detail && <code className={styles.issueDetail}>{entry.detail}</code>}
          </span>
        </div>
      ))}
    </section>
  );
}

function Visibility(): ReactNode {
  return (
    <dl className={styles.visibility}>
      <div>
        <dt>Application</dt>
        <dd>{visibility.application}</dd>
      </div>
      <div>
        <dt>Capabilities</dt>
        <dd className={styles.chips}>
          {visibility.capabilities.map((capability) => (
            <span key={capability} className={styles.capability}>
              {capability}
            </span>
          ))}
        </dd>
      </div>
      <div>
        <dt>Values from</dt>
        <dd className={styles.chips}>
          {visibility.sources.map((source) => (
            <span key={source.label} className={`${styles.source} ${source.seen ? '' : styles.absent}`}>
              <i aria-hidden="true" />
              {source.label}
              {!source.seen && <span className={styles.hidden}> (not visible)</span>}
            </span>
          ))}
        </dd>
      </div>
    </dl>
  );
}

function VisibilityPanel(): ReactNode {
  return (
    <section className={styles.panel}>
      <p className={styles.panelHead}>What this run could see</p>
      <div className={styles.panelBody}>
        <Visibility />
      </div>
    </section>
  );
}

function RunClock(): ReactNode {
  return (
    <section className={styles.panel}>
      <p className={styles.panelHead}>Run timeline</p>
      <div className={styles.clockHead} aria-hidden="true">
        <span />
        <span className={styles.axis}>
          <i>start</i>
          <i>+4.06 s</i>
        </span>
        <span />
      </div>
      {runClock.map((row) => (
        <div key={row.number} className={styles.clockRow}>
          <b className={styles[row.outcome]}>{row.number}</b>
          <span className={styles.clockName}>{row.title}</span>
          <span className={styles.clockTrack}>
            {row.phases.map(([phase, start, length]) => (
              <i key={phase} style={{...place(onRun(start, length)), background: `var(--phase-${phase})`}} />
            ))}
            {row.gap && <Hatch at={onRun(row.gap[0], row.gap[1])} />}
          </span>
          <Dot outcome={row.outcome} />
        </div>
      ))}
    </section>
  );
}

function RunDetails(): ReactNode {
  return (
    <section className={styles.panel}>
      <p className={styles.panelHead}>Run details</p>
      <dl className={styles.properties}>
        <div>
          <dt>Run id</dt>
          <dd>{run.id}</dd>
        </div>
        {run.environment.map(([key, value]) => (
          <div key={key}>
            <dt>{key}</dt>
            <dd>{value}</dd>
          </div>
        ))}
      </dl>
    </section>
  );
}

function RunScreen({view}: {view: 'overview' | 'timeline' | 'details'}): ReactNode {
  const active = view === 'overview' ? 'Overview' : view === 'timeline' ? 'Timeline' : 'Details';
  return (
    <div className={styles.screen}>
      <RunHeader />
      <Tabs items={run.tabs} active={active} />
      {view === 'overview' && (
        <div className={styles.overview}>
          <Attention />
          <VisibilityPanel />
        </div>
      )}
      {view === 'timeline' && <RunClock />}
      {view === 'details' && <RunDetails />}
    </div>
  );
}

/* ── A test ───────────────────────────────────────────────────────────────────────────────── */

function TestHeader(): ReactNode {
  return (
    <header className={styles.testHead}>
      <span className={styles.stepper} aria-hidden="true">
        <i>‹</i>
        <b>{test.number}</b>
        <i>›</i>
      </span>
      <span className={styles.testName}>
        <strong>{test.title}</strong>
        <small>
          {test.group} <code>{test.method}</code>
        </small>
      </span>
      <span className={styles.outcome}>
        <Dot outcome="failed" />
        Failed <em>1.16 s</em>
      </span>
    </header>
  );
}

function Verdict(): ReactNode {
  const verdict = test.verdict;
  return (
    <section className={styles.verdict} aria-label="Why this test did not pass">
      <span className={styles.rule}>{verdict.rule}</span>
      <strong>{verdict.what}</strong>
      <span className={styles.muted}>on {verdict.on}</span>
      <code>
        {verdict.path}: expected {verdict.expected}, got <b>{verdict.actual}</b>
      </code>
    </section>
  );
}

function PhaseBand(): ReactNode {
  return (
    <>
      <div className={styles.band} aria-hidden="true">
        {test.phases.map((phase) => (
          <i key={phase.phase} style={{...place(phase.at), background: `var(--phase-${phase.phase})`}} />
        ))}
        <Hatch at={test.gap.at} />
      </div>
      <p className={styles.legend}>
        {test.phases.map((phase) => (
          <span key={phase.phase} style={marker(`--phase-${phase.phase}`)}>
            {phase.label} <b>{phase.duration}</b>
            {phase.untraced && `, ${phase.untraced}`}
          </span>
        ))}
      </p>
    </>
  );
}

type Framework = 'Show' | 'Dim' | 'Hide';

function TestTabs({active, framework}: {active: string; framework?: Framework}): ReactNode {
  return (
    <div className={styles.testTabs}>
      <Tabs items={['Steps', 'Timeline', 'State', 'Evidence']} active={active} />
      {framework && (
        <span className={styles.framework} aria-hidden="true">
          Framework
          {(['Show', 'Dim', 'Hide'] as Framework[]).map((mode) => (
            <span key={mode} className={mode === framework ? styles.segmentOn : undefined}>
              {mode}
            </span>
          ))}
        </span>
      )}
    </div>
  );
}

function TestFrame({active, framework, children}: {active: string; framework?: Framework; children: ReactNode}): ReactNode {
  return (
    <div className={styles.screen}>
      <TestHeader />
      <Verdict />
      <PhaseBand />
      <TestTabs active={active} framework={framework} />
      {children}
    </div>
  );
}

function StepRow({step}: {step: Step}): ReactNode {
  return (
    <div className={styles.nest} style={{'--depth': step.depth} as CSSProperties}>
      <div className={`${styles.line} ${step.failed ? styles.lineFailed : ''}`}>
        {step.leaf ? <span /> : <Chevron open={step.open} />}
        <span className={styles.pick}>
          <span className={styles.kind} style={tone(step.tone)}>
            {step.kind}
          </span>
          <span className={styles.title}>{step.title}</span>
          {(step.facts || step.app) && (
            <span className={styles.facts}>
              {step.app && <b className={styles.app}>app</b>}
              {step.facts}
            </span>
          )}
        </span>
        <span className={styles.checks}>
          {step.checks?.map((item) => (
            <span key={item.label} className={item.passed ? styles.check : styles.checkFailed}>
              <b aria-hidden="true">{item.passed ? '✓' : '×'}</b>
              {item.label}
            </span>
          ))}
        </span>
        <span className={styles.bar}>
          <i style={{...place(step.at), background: step.failed ? 'var(--danger)' : undefined}} />
        </span>
        <span className={styles.duration}>{step.duration}</span>
      </div>
    </div>
  );
}

function PhaseHead({phase, open}: {phase: (typeof test.phases)[number]; open?: boolean}): ReactNode {
  return (
    <div className={styles.phaseHead} style={marker(`--phase-${phase.phase}`)}>
      <Chevron open={open} />
      <i className={styles.marker} aria-hidden="true" />
      <strong>{phase.label}</strong>
      <span className={styles.summary}>{phase.summary}</span>
      {phase.failed && <b className={styles.failed}>Failed</b>}
      <small>{phase.duration}</small>
    </div>
  );
}

function StepsScreen(): ReactNode {
  const [setup, execution, teardown] = test.phases;
  return (
    <TestFrame active="Steps" framework="Dim">
      <div className={styles.phases}>
        <section className={styles.phase}>
          <PhaseHead phase={setup} />
        </section>
        <section className={styles.phase}>
          <PhaseHead phase={execution} open />
          <div className={styles.rows}>
            {bodySteps.map((step) => (
              <StepRow key={step.title} step={step} />
            ))}
            <div className={`${styles.line} ${styles.gap}`}>
              <i className={styles.gapMark} aria-hidden="true" />
              <span className={styles.gapText}>
                <strong>{test.gap.duration} with no recorded operation</strong>
                <small>{test.gap.hint}</small>
              </span>
              <span className={styles.checks} />
              <span className={styles.bar}>
                <Hatch at={test.gap.at} />
              </span>
              <span className={styles.duration}>{test.gap.duration}</span>
            </div>
            <StepRow step={callStep} />
            <p className={styles.observed}>
              Observed {observed.what} <span>on {observed.on}</span>
            </p>
          </div>
        </section>
        <section className={styles.phase}>
          <PhaseHead phase={teardown} />
        </section>
      </div>
    </TestFrame>
  );
}

function TimelineScreen(): ReactNode {
  return (
    <TestFrame active="Timeline" framework="Dim">
      <div className={styles.toolbar} aria-hidden="true">
        <span className={styles.search}>Find by name, kind or attribute</span>
        <Chip label="All" count={timeline.all} on />
        <Chip label="Needs attention" count={timeline.attention} danger />
        <span className={styles.toolLabel}>Zoom</span>
        <Chip label="Whole test" on />
        <Chip label="Setup" />
        <Chip label="Execution" />
        <Chip label="Teardown" />
      </div>
      <section className={styles.panel}>
        <div className={styles.timeHead} aria-hidden="true">
          <span>{timeline.shown}</span>
          <span className={styles.ruler}>
            {test.phases.map((phase) => (
              <span
                key={phase.phase}
                className={styles.rulerPhase}
                style={{...place(phase.at), ...marker(`--phase-${phase.phase}`)}}
              >
                {phase.label}
              </span>
            ))}
            {test.ticks.map(([label, left]) => (
              <span key={label} className={styles.tick} style={{left: `${left}%`}}>
                {label}
              </span>
            ))}
          </span>
          <span className={styles.durationHead}>Duration</span>
        </div>
        <div className={styles.timeRows}>
          {timeline.rows.map((row) => (
            <div
              key={row.name}
              className={`${styles.timeRow} ${row.family === 'failed' ? styles.timeFailed : ''} ${row.dim ? styles.timeDim : ''}`}
              style={{'--depth': row.depth} as CSSProperties}
            >
              <span className={styles.timeLabel}>
                {row.open === undefined ? <span className={styles.fold} /> : <Chevron open={row.open} />}
                {row.family === 'untraced' ? (
                  <span className={styles.gapName}>{row.name}</span>
                ) : (
                  <>
                    <span className={styles.kind} style={tone(row.tone ?? '--muted')}>
                      {row.kind}
                    </span>
                    <span className={styles.timeName}>{row.name}</span>
                    {row.marks && <span className={styles.events}>{row.marks}</span>}
                  </>
                )}
              </span>
              <span className={styles.track}>
                <Hatch at={test.gap.at} className={styles.zone} />
                {row.family === 'untraced' ? (
                  <Hatch at={row.at} className={styles.timeBar} />
                ) : (
                  <i className={`${styles.timeBar} ${styles[`family-${row.family}`]}`} style={place(row.at)} />
                )}
              </span>
              <span className={styles.duration}>{row.duration}</span>
            </div>
          ))}
        </div>
      </section>
      <Legend
        items={[
          ['Work the test did', '--type-action'],
          ['Check', '--type-evidence'],
          ['Framework', '--border-strong'],
          ['Failed', '--danger'],
        ]}
      />
    </TestFrame>
  );
}

function StateScreen(): ReactNode {
  return (
    <TestFrame active="State" framework="Hide">
      <p className={styles.selection}>
        <strong>{state.selected.name}</strong>, {state.selected.when}
      </p>
      {state.groups.map((group) => (
        <section key={group.title} className={styles.panel}>
          <p className={styles.panelHead}>{group.title}</p>
          <div className={styles.stateScale} aria-hidden="true">
            <span />
            <span className={styles.ruler}>
              {state.ticks.map(([label, left]) => (
                <span key={label} className={styles.tick} style={{left: `${left}%`}}>
                  {label}
                </span>
              ))}
            </span>
          </div>
          {group.items.map((item) => (
            <div key={item.name} className={`${styles.item} ${item.related ? styles.itemRelated : ''}`}>
              <span className={styles.itemName}>
                <span className={styles.itemKind}>{item.kind}</span>
                <strong>{item.name}</strong>
                <small>{item.facts}</small>
              </span>
              <span className={styles.lifeTrack}>
                <i className={styles.selectedTime} style={place(state.selected.at)} />
                <i className={styles.life} style={place(item.life)} />
                {item.ticks.map(([left, source], index) => (
                  <b key={index} className={`${styles.stateTick} ${styles[source]}`} style={{left: `${left}%`}} />
                ))}
              </span>
            </div>
          ))}
        </section>
      ))}
      <Legend
        items={[
          ['Test side', '--muted'],
          ['Observed', '--pt-cyan'],
          ['Application', '--blueprint'],
        ]}
      />
    </TestFrame>
  );
}

function EvidenceScreen(): ReactNode {
  return (
    <TestFrame active="Evidence">
      <div className={styles.toolbar} aria-hidden="true">
        {evidence.counts.map(([label, count], index) => (
          <Chip key={label} label={label} count={count} on={index === 0} />
        ))}
      </div>
      <section className={styles.panel}>
        {evidence.entries.map((entry, index) => (
          <div key={index} className={`${styles.entry} ${styles[`entry-${entry.kind}`]}`}>
            <span className={styles.offset}>{entry.at}</span>
            <span className={styles.entryKind}>{entry.kind}</span>
            <span className={styles.entryWhat}>
              <strong>{entry.name}</strong>
              <small>{entry.detail}</small>
            </span>
            {entry.open ? <span className={styles.open}>Open</span> : <span />}
            <span className={styles.from}>{entry.from}</span>
          </div>
        ))}
      </section>
    </TestFrame>
  );
}

/* ── The details ──────────────────────────────────────────────────────────────────────────── */

/** The index opens a folded part before it jumps to it, as the inspector does. */
function openFold(id: string): void {
  const target = document.getElementById(id);
  if (target instanceof HTMLDetailsElement) target.open = true;
}

function CheckScreen(): ReactNode {
  const id = useId();
  const source = check.source;
  return (
    <div className={`${styles.screen} ${styles.details}`}>
      <p className={styles.path}>
        {check.path[0]} / <span>{check.path[1]}</span>
      </p>
      <header className={styles.detailsHead} title={check.hint}>
        <span className={styles.kindChip} style={tone('--type-assertion')}>
          {check.kind}
        </span>
        <strong>{check.title}</strong>
      </header>
      <p className={styles.detailsFacts}>
        <span className={styles.failed}>
          <Dot outcome="failed" />
          Failed
        </span>
        {check.facts.map((fact) => (
          <span key={fact}>{fact}</span>
        ))}
      </p>

      <nav className={styles.index} aria-label="Parts of this operation">
        <a className={styles.indexHot} href={`#${id}-comparison`}>
          Comparison
        </a>
        <a href={`#${id}-source`}>Source</a>
        <a href={`#${id}-evidence`}>Evidence</a>
        <a href={`#${id}-attributes`} onClick={() => openFold(`${id}-attributes`)}>
          Attributes
        </a>
      </nav>

      <section className={styles.card} id={`${id}-comparison`}>
        <header>
          <strong>Validated document</strong>
          <small>
            <b className={styles.passed}>✓</b> matched <b className={styles.failed}>×</b> expected, then actual
          </small>
        </header>
        <div className={styles.mismatch}>
          <i>×</i> <span className={styles.property}>{check.mismatch.property}</span>
          <span className={styles.muted}>:</span> <s>{check.mismatch.expected}</s> →{' '}
          <strong>{check.mismatch.actual}</strong>
        </div>
      </section>
      <details className={styles.foldLine}>
        <summary>The response it judged</summary>
        <pre className={styles.foldBody}>{check.response}</pre>
      </details>

      <section className={styles.card} id={`${id}-source`}>
        <header>
          <strong className={styles.mono}>
            {source.file}:{source.line}
          </strong>
          <small>{source.method}</small>
        </header>
        <pre className={styles.code}>
          {source.lines.map(([number, text]) => (
            <span key={number} className={number === source.line ? styles.current : undefined}>
              <b>{number}</b>
              {text || ' '}
            </span>
          ))}
        </pre>
      </section>

      <section className={styles.block} id={`${id}-evidence`}>
        <p className={styles.blockHead}>Evidence</p>
        <div className={styles.fileRow}>
          <span>File</span>
          <strong>{check.file.name}</strong>
          <small>{check.file.detail}</small>
        </div>
      </section>

      <details className={styles.foldLine}>
        <summary>
          Exception <small>{check.exception.type}</small>
        </summary>
        <p className={styles.foldBody}>{check.exception.message}</p>
      </details>
      <details className={styles.foldLine} id={`${id}-attributes`}>
        <summary>
          Attributes <small>{check.attributes}</small>
        </summary>
        <p className={styles.foldBody}>
          The shape check&apos;s {check.attributes} recorded attributes; open the run in the viewer to read them.
        </p>
      </details>
    </div>
  );
}

/** One screen of the viewer, for a page to show. */
export default function ViewerMock({screen}: {screen: ViewerScreen}): ReactNode {
  switch (screen) {
    case 'run':
      return <RunScreen view="overview" />;
    case 'run-timeline':
      return <RunScreen view="timeline" />;
    case 'run-details':
      return <RunScreen view="details" />;
    case 'visibility':
      return (
        <div className={styles.screen}>
          <RunHeader />
          <p className={styles.subhead}>What this run could see</p>
          <Visibility />
        </div>
      );
    case 'steps':
      return <StepsScreen />;
    case 'timeline':
      return <TimelineScreen />;
    case 'state':
      return <StateScreen />;
    case 'evidence':
      return <EvidenceScreen />;
    case 'check':
      return <CheckScreen />;
  }
}
