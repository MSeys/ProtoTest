import type {ReactNode} from 'react';
import useBaseUrl from '@docusaurus/useBaseUrl';

import styles from './styles.module.css';

/*
 * Every swatch below is drawn from the live token file (design/prototest-tokens.css), which custom.css
 * imports. Nothing here restates a colour, so this page cannot drift from the viewer or the report.
 */

export function Swatches({tokens}: {tokens: string[]}): ReactNode {
  return (
    <div className={styles.swatches}>
      {tokens.map((token) => (
        <div key={token} className={styles.swatch}>
          <i style={{background: `var(${token})`}} />
          <code>{token}</code>
        </div>
      ))}
    </div>
  );
}

const phases = ['Setup', 'Execution', 'Rollback', 'Teardown'];

export function Phases(): ReactNode {
  return (
    <div className={styles.phases}>
      {phases.map((phase) => (
        <span key={phase}>
          <i style={{background: `var(--phase-${phase.toLowerCase()})`}} />
          {phase}
        </span>
      ))}
    </div>
  );
}

const families: {family: string; token: string; means: string; kinds: string[]}[] = [
  {family: 'Action', token: '--type-action', means: 'What the test did', kinds: ['Call', 'Data', 'your own kinds']},
  {family: 'Evidence', token: '--type-evidence', means: 'What it proved or produced', kinds: ['Assertion', 'Observation', 'Artifact']},
  {family: 'Verdict', token: '--type-verdict', means: 'What it decided', kinds: ['Finding', 'Gate']},
  {family: 'Framework', token: '--type-framework', means: 'The machinery that carried it', kinds: ['Lifecycle', 'Extension', 'Client', 'Context', 'Authentication', 'Ownership']},
];

export function TypeFamilies(): ReactNode {
  return (
    <div className={styles.families}>
      {families.map((item) => (
        <div key={item.family} className={styles.family} style={{['--node' as string]: `var(${item.token})`}}>
          <strong>{item.family}</strong>
          <span>{item.means}</span>
          <div>
            {item.kinds.map((kind) => (
              <em key={kind} className={styles.chip}>{kind}</em>
            ))}
          </div>
        </div>
      ))}
    </div>
  );
}

const outcomes = [
  {name: 'Succeeded', token: '--outcome-succeeded'},
  {name: 'Partial', token: '--outcome-partial'},
  {name: 'Failed', token: '--outcome-failed'},
  {name: 'Cancelled', token: '--outcome-cancelled'},
  {name: 'Skipped', token: '--outcome-skipped'},
];

export function Outcomes(): ReactNode {
  return (
    <div className={styles.phases}>
      {outcomes.map((outcome) => (
        <span key={outcome.name} style={{color: `var(${outcome.token})`}}>
          <b className={styles.dot} style={{background: `var(${outcome.token})`}} />
          {outcome.name}
        </span>
      ))}
    </div>
  );
}

const scale = [
  ['--text-display', 'Display'],
  ['--text-heading', 'Heading'],
  ['--text-title', 'Title'],
  ['--text-strong', 'Strong'],
  ['--text-body', 'Body'],
  ['--text-meta', 'Meta'],
  ['--text-micro', 'Micro'],
];

export function TypeScale(): ReactNode {
  return (
    <div className={styles.scale}>
      {scale.map(([token, label]) => (
        <div key={token}>
          <span style={{fontSize: `var(${token})`}}>{label}</span>
          <code>{token}</code>
        </div>
      ))}
    </div>
  );
}

export function BrandMarks(): ReactNode {
  const paper = useBaseUrl('/img/brand/prototest-mark.svg');
  const blueprint = useBaseUrl('/img/brand/prototest-mark-white.svg');
  return (
    <div className={styles.marks}>
      <figure className={styles.paper}>
        <img src={paper} alt="ProtoTest mark on paper" />
        <figcaption>Technical paper — navy and blueprint blue</figcaption>
      </figure>
      <figure className={styles.blueprint}>
        <img src={blueprint} alt="ProtoTest mark on the blueprint" />
        <figcaption>Execution blueprint — paper and cyan</figcaption>
      </figure>
    </div>
  );
}
