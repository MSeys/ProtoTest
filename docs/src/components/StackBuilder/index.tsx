import {useMemo, useState, type ReactNode} from 'react';

import styles from './styles.module.css';

interface Capability {
  id: string;
  label: string;
  note: string;
  packages: string[];
}

const capabilities: Capability[] = [
  {id: 'rest', label: 'Call an API', note: 'REST requests, responses and shape checks', packages: ['ProtoTest.Rest']},
  {id: 'openapi', label: 'Prove the contract', note: 'OpenAPI response and shape coverage', packages: ['ProtoTest.OpenApi']},
  {id: 'graphql', label: 'Use GraphQL', note: 'Queries, mutations and subscriptions', packages: ['ProtoTest.GraphQL']},
  {id: 'grpc', label: 'Use gRPC', note: 'Unary calls and streams', packages: ['ProtoTest.Grpc']},
  {id: 'messaging', label: 'Follow an event', note: 'Publish and await messages', packages: ['ProtoTest.Messaging']},
  {id: 'database', label: 'Inspect the database', note: 'SQL through an EF Core context', packages: ['ProtoTest.Sql.EntityFrameworkCore']},
  {id: 'browser', label: 'Drive the browser', note: 'Page objects and Playwright', packages: ['ProtoTest.Web.Playwright']},
  {id: 'sheets', label: 'Check a workbook', note: 'Cells, ranges and typed models', packages: ['ProtoTest.Sheets']},
  {id: 'data', label: 'Own test data', note: 'Deterministic builders and provisioners', packages: ['ProtoTest.Data']},
  {id: 'host', label: 'Host ASP.NET Core', note: 'Run the application in-process', packages: ['ProtoTest.AspNetCore']},
];

const runners = ['NUnit', 'Xunit', 'Xunit3', 'MSTest', 'TUnit'] as const;

export default function StackBuilder(): ReactNode {
  const [selected, setSelected] = useState<string[]>(['rest', 'host']);
  const [runner, setRunner] = useState<(typeof runners)[number]>('NUnit');
  const [copied, setCopied] = useState(false);

  const packages = useMemo(() => {
    const chosen = capabilities
      .filter((capability) => selected.includes(capability.id))
      .flatMap((capability) => capability.packages);
    return [`ProtoTest.${runner}`, ...new Set(chosen)];
  }, [runner, selected]);

  const commands = packages.map((name) => `dotnet add package ${name}`).join('\n');

  function toggle(id: string): void {
    setCopied(false);
    setSelected((current) =>
      current.includes(id) ? current.filter((value) => value !== id) : [...current, id],
    );
  }

  async function copy(): Promise<void> {
    await navigator.clipboard.writeText(commands);
    setCopied(true);
  }

  return (
    <section className={styles.builder} aria-labelledby="stack-builder-title">
      <header className={styles.head}>
        <div>
          <span className={styles.eyebrow}>COMPOSE YOUR SUITE</span>
          <h2 id="stack-builder-title">Start with what the scenario crosses</h2>
        </div>
        <p>Pick capabilities, not a bundle. They meet again in the same execution context and trace.</p>
      </header>

      <div className={styles.body}>
        <div className={styles.choices}>
          <fieldset>
            <legend>1. What does the test cross?</legend>
            <div className={styles.capabilities}>
              {capabilities.map((capability) => {
                const active = selected.includes(capability.id);
                return (
                  <button
                    key={capability.id}
                    type="button"
                    className={active ? styles.capabilityActive : styles.capability}
                    aria-pressed={active}
                    onClick={() => toggle(capability.id)}>
                    <span className={styles.check} aria-hidden="true">{active ? '✓' : '+'}</span>
                    <span>
                      <strong>{capability.label}</strong>
                      <small>{capability.note}</small>
                    </span>
                  </button>
                );
              })}
            </div>
          </fieldset>

          <fieldset>
            <legend>2. Which runner do you use?</legend>
            <div className={styles.runners}>
              {runners.map((name) => (
                <button
                  key={name}
                  type="button"
                  className={runner === name ? styles.runnerActive : styles.runner}
                  aria-pressed={runner === name}
                  onClick={() => { setRunner(name); setCopied(false); }}>
                  {name === 'Xunit' ? 'xUnit v2' : name === 'Xunit3' ? 'xUnit v3' : name}
                </button>
              ))}
            </div>
          </fieldset>
        </div>

        <aside className={styles.result} data-surface="blueprint" aria-live="polite">
          <div className={styles.resultHead}>
            <span>YOUR FOUNDATION</span>
            <code>{packages.length} direct {packages.length === 1 ? 'package' : 'packages'}</code>
          </div>
          <pre><code>{commands}</code></pre>
          <button type="button" className={styles.copy} onClick={copy}>
            {copied ? 'Copied' : 'Copy commands'}
          </button>
          <p><code>ProtoTest.Core</code> and shared plumbing arrive transitively. Add container adapters only when the run should own the real dependency.</p>
        </aside>
      </div>
    </section>
  );
}
