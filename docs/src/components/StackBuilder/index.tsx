import {useMemo, useState, type ReactNode} from 'react';

import CopyCode from '@site/src/components/CopyCode';
import catalog from '@site/src/data/integrations.json';
import styles from './styles.module.css';

interface Choice {
  id: string;
  label: string;
  note: string;
  packages: string[];
  preview: boolean;
}

/*
 * The choices come from the integrations catalog, the same file the integration cards read, so a package
 * added there is offered here too. Each choice names a task and the direct references it needs.
 */
const groups = Object.entries(catalog)
  .map(([key, items]) => ({
    key,
    label: key
      .replace(/-preview$/, '')
      .replace(/-/g, ' ')
      .replace(/^./, (first) => first.toUpperCase()),
    choices: items.flatMap((item): Choice[] =>
      'builder' in item && item.builder
        ? item.builder.map((choice) => ({...choice, preview: item.preview}))
        : [],
    ),
  }))
  .filter((group) => group.choices.length > 0);
const choices = groups.flatMap((group) => group.choices);

const runners = catalog.runners[0].packages.map((name) => name.replace('ProtoTest.', ''));
const runnerLabels: Record<string, string> = {Xunit: 'xUnit v2', Xunit3: 'xUnit v3'};

export default function StackBuilder(): ReactNode {
  const [selected, setSelected] = useState<string[]>(['rest', 'aspnetcore']);
  const [runner, setRunner] = useState('NUnit');

  const packages = useMemo(() => {
    const chosen = selected.flatMap((id) => choices.find((choice) => choice.id === id)?.packages ?? []);
    return [`ProtoTest.${runner}`, ...new Set(chosen)];
  }, [runner, selected]);

  const commands = packages.map((name) => `dotnet add package ${name}`).join('\n');

  function toggle(id: string): void {
    setSelected((current) =>
      current.includes(id) ? current.filter((value) => value !== id) : [...current, id],
    );
  }

  return (
    // The page's own heading introduces the builder; a second title inside it would only repeat it.
    <section className={styles.builder} aria-label="Package builder">
      <div className={styles.body}>
        <div className={styles.choices}>
          <fieldset>
            <legend>1. What does the test cross?</legend>
            {groups.map((group) => (
              <div key={group.key} className={styles.group}>
                <span className={styles.groupLabel}>{group.label}</span>
                <div className={styles.capabilities}>
                  {group.choices.map((choice) => {
                    const active = selected.includes(choice.id);
                    return (
                      <button
                        key={choice.id}
                        type="button"
                        className={active ? styles.capabilityActive : styles.capability}
                        aria-pressed={active}
                        onClick={() => toggle(choice.id)}
                      >
                        <span className={styles.check} aria-hidden="true">
                          {active ? '✓' : '+'}
                        </span>
                        <span>
                          <strong>
                            {choice.label}
                            {choice.preview && <em className={styles.preview}>Preview</em>}
                          </strong>
                          <small>{choice.note}</small>
                        </span>
                      </button>
                    );
                  })}
                </div>
              </div>
            ))}
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
                  onClick={() => {
                    setRunner(name);
                  }}
                >
                  {runnerLabels[name] ?? name}
                </button>
              ))}
            </div>
          </fieldset>
        </div>

        <aside className={styles.result} data-surface="blueprint" aria-live="polite">
          <div className={styles.resultHead}>
            <strong>Packages</strong>
            <small>
              {packages.length} direct {packages.length === 1 ? 'reference' : 'references'}
            </small>
            <CopyCode text={commands} label="Copy commands" />
          </div>
          <pre>
            <code>{commands}</code>
          </pre>
          <p>
            <code>ProtoTest.Core</code> and shared plumbing arrive transitively. Add container adapters only
            when the run should own the real dependency.
          </p>
        </aside>
      </div>
    </section>
  );
}
