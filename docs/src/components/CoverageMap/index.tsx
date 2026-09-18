import type {ReactNode} from 'react';

import Frame from '@site/src/components/Frame';
import styles from './styles.module.css';

type State = 'covered' | 'partial' | 'uncovered';

interface Property {
  label: string;
  state?: State;
  note?: string;
}

interface Response {
  label: string;
  state?: State;
  note?: string;
  properties?: Property[];
}

interface Endpoint {
  label: string;
  state?: State;
  note?: string;
  responses?: Response[];
}

/**
 * The shape of what OpenApiCoverageCollector reports: every endpoint in the spec, every declared response
 * and every response property — each covered, partially covered, or never reached. The states are the
 * report's, not a tick and a circle.
 */
const endpoints: Endpoint[] = [
  {
    label: 'GET /api/control-plane',
    note: '12 hits',
    responses: [
      {
        label: '200',
        note: '12 hits',
        properties: [
          {label: '$.workspaceCount'},
          {label: '$.releaseCount'},
          {label: '$.monthlyRecurringRevenue'},
          {label: '$.trialEndsAt', state: 'uncovered', note: 'never asserted'},
        ],
      },
      {label: '403', state: 'uncovered', note: 'never reached'},
    ],
  },
  {label: 'POST /api/workspaces', note: '6 hits'},
  {label: 'DELETE /api/workspaces/{id}', state: 'uncovered', note: 'never called'},
];

const stateLabel: Record<State, string> = {
  covered: 'Covered',
  partial: 'Partial',
  uncovered: 'Uncovered',
};

function resolve(...states: State[]): State {
  if (states.length === 0) return 'covered';
  if (states.every((state) => state === 'covered')) return 'covered';
  if (states.every((state) => state === 'uncovered')) return 'uncovered';
  return 'partial';
}

function Row({label, state, note}: {label: string; state: State; note?: string}): ReactNode {
  return (
    <div className={styles.row}>
      <i className={`${styles.dot} ${styles[state]}`} />
      <span className={styles.label}>{label}</span>
      {note && <span className={styles.noteText}>{note}</span>}
      <span className={`${styles.badge} ${styles[state]}`}>{stateLabel[state]}</span>
    </div>
  );
}

function propertyState(property: Property): State {
  return property.state ?? 'covered';
}

export default function CoverageMap(): ReactNode {
  return (
    <Frame
      head={
        <>
          <span className={styles.headTitle}>OpenAPI coverage</span>
          <span className={styles.headMeta}>control-plane.openapi.json</span>
        </>
      }
      foot={
        <>
          Property-level hits come from <code>ShouldMatchShape</code> — the paths your assertion actually
          matched.
        </>
      }>
      <div className={styles.rows}>
        {endpoints.map((endpoint) => {
          const responses = endpoint.responses ?? [];
          const endpointState =
            endpoint.state ??
            resolve(
              ...responses.map((response) => {
                const properties = response.properties ?? [];
                return (
                  response.state ??
                  (properties.length === 0 ? 'covered' : resolve(...properties.map(propertyState)))
                );
              }),
            );

          return (
            <div key={endpoint.label}>
              <Row label={endpoint.label} state={endpointState} note={endpoint.note} />
              {responses.length > 0 && (
                <div className={styles.children}>
                  {responses.map((response) => {
                    const properties = response.properties ?? [];
                    const responseState =
                      response.state ??
                      (properties.length === 0 ? 'covered' : resolve(...properties.map(propertyState)));

                    return (
                      <div key={response.label}>
                        <Row label={response.label} state={responseState} note={response.note} />
                        {properties.length > 0 && (
                          <div className={styles.children}>
                            {properties.map((property) => (
                              <Row
                                key={property.label}
                                label={property.label}
                                state={propertyState(property)}
                                note={property.note}
                              />
                            ))}
                          </div>
                        )}
                      </div>
                    );
                  })}
                </div>
              )}
            </div>
          );
        })}
      </div>
    </Frame>
  );
}
