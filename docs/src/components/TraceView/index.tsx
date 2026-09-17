import type {ReactNode} from 'react';

import styles from './styles.module.css';

interface Entry {
  tree: string;
  kind?: string;
  hookKind?: boolean;
  label: string;
  duration?: string;
  phase?: boolean;
}

/**
 * Illustrates a per-test ProtoTrace tree. Entry kinds are the real ones
 * emitted by ProtoExecutionContext, ProtoTest.Rest and the runner hooks.
 */
const entries: Entry[] = [
  {tree: '', label: 'Setup', phase: true},
  {tree: '├─', kind: 'hook', hookKind: true, label: 'SampleEnvironment · provision tenant', duration: '210ms'},
  {tree: '├─', kind: 'context.set', label: 'SampleUserContext', duration: '2ms'},
  {tree: '└─', kind: 'client.resolve', label: 'Api', duration: '14ms'},
  {tree: '', label: 'Execution', phase: true},
  {tree: '├─', kind: 'rest.builder.create', label: 'GET /api/control-plane', duration: '1ms'},
  {tree: '│  └─', kind: 'assert.json.shape', label: '4 properties matched', duration: '3ms'},
  {tree: '└─', kind: 'attachment.register', label: 'response.json', duration: ''},
  {tree: '', label: 'Teardown', phase: true},
  {tree: '└─', kind: 'hook', hookKind: true, label: 'SampleEnvironment · delete tenant', duration: '38ms'},
];

export default function TraceView(): ReactNode {
  return (
    <div className={styles.frame}>
      <div className={styles.head}>
        <span className={styles.headFile}>Control_plane_reports_the_tenant_rollup</span>
        <span className={styles.passPill}>✓ passed · 288ms</span>
      </div>
      <div className={styles.rows}>
        {entries.map((entry, index) =>
          entry.phase ? (
            <div key={index} className={`${styles.row} ${styles.rowPhase}`}>
              {entry.label}
            </div>
          ) : (
            <div key={index} className={styles.row}>
              <span className={styles.tree}>{entry.tree}</span>
              <span className={`${styles.kind} ${entry.hookKind ? styles.kindHook : ''}`}>
                {entry.kind}
              </span>
              <span className={styles.label}>{entry.label}</span>
              <span className={styles.duration}>{entry.duration}</span>
            </div>
          ),
        )}
      </div>
      <div className={styles.foot}>
        exported to <span className={styles.footArchive}>prototest-demo.prototrace</span> — open it
        at trace.prototest.dev
      </div>
    </div>
  );
}
