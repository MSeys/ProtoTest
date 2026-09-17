import type {ReactNode} from 'react';

import styles from './styles.module.css';

type Level = 'endpoint' | 'response' | 'property';

interface Row {
  level: Level;
  label: string;
  covered: boolean;
  note?: string;
}

/**
 * Illustrates the shape of what OpenApiCoverageCollector reports:
 * every endpoint in the spec, every declared response, and every
 * response property — each marked covered or never asserted.
 */
const rows: Row[] = [
  {level: 'endpoint', label: 'GET /api/control-plane', covered: true, note: '12 hits'},
  {level: 'response', label: '200', covered: true, note: '12 hits'},
  {level: 'property', label: '$.workspaceCount', covered: true},
  {level: 'property', label: '$.releaseCount', covered: true},
  {level: 'property', label: '$.monthlyRecurringRevenue', covered: true},
  {level: 'property', label: '$.trialEndsAt', covered: false, note: 'never asserted'},
  {level: 'response', label: '403', covered: false, note: 'never reached'},
  {level: 'endpoint', label: 'POST /api/workspaces', covered: true, note: '6 hits'},
  {level: 'endpoint', label: 'DELETE /api/workspaces/{id}', covered: false, note: 'never called'},
];

export default function CoverageMap(): ReactNode {
  return (
    <div className={styles.frame}>
      <div className={styles.head}>
        <span className={styles.headTitle}>OpenAPI coverage</span>
        <span className={styles.headMeta}>control-plane.openapi.json</span>
      </div>
      <div className={styles.rows}>
        {rows.map((row, index) => (
          <div
            key={index}
            className={`${styles.row} ${styles[row.level]} ${row.covered ? '' : styles.uncovered}`}>
            <span className={`${styles.mark} ${row.covered ? styles.markOk : styles.markMiss}`}>
              {row.covered ? '✓' : '○'}
            </span>
            <span className={styles.label}>{row.label}</span>
            {row.note && (
              <span className={`${styles.note} ${row.covered ? '' : styles.noteMiss}`}>{row.note}</span>
            )}
          </div>
        ))}
      </div>
      <div className={styles.foot}>
        Property-level hits come from <code>ShouldMatchShape</code> — the paths your assertion
        actually matched.
      </div>
    </div>
  );
}
