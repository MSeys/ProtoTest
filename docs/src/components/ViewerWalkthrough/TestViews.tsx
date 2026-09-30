import type {ReactNode} from 'react';
import styles from './styles.module.css';

const duration = 1162.6093;
const place = (start: number, length: number) => ({left: `${start / duration * 100}%`, width: `max(2px, ${length / duration * 100}%)`});
const operations = [
  {name: 'Setup', start: 0, length: 46.325, phase: 'setup'},
  {name: 'Test execution', start: 46.349, length: 1108.669, phase: 'execution'},
  {name: 'Create · IssueInvoiceRequest', start: 47.245, length: 90.971, phase: 'execution'},
  {name: '1.01 s with no recorded operation', start: 138.216, length: 1006.522, phase: 'execution', gap: true},
  {name: 'REST · GET /api/v1/organization', start: 1144.738, length: 7.552, phase: 'execution'},
  {name: 'Assert status · 200 OK', start: 1152.421, length: 0.009, phase: 'execution'},
  {name: 'Assert response shape', start: 1152.495, length: 2.2, phase: 'execution', failed: true},
  {name: 'Teardown', start: 1155.097, length: 7.396, phase: 'teardown'},
];
const items = [
  {name: 'Invoice INV-202610-0001', start: 134.709, end: 134.711, selected: true, facts: 'status open, total 238,80', changes: [134.711]},
  {name: "TenantResponse 'northstar-597539000012'", start: 46.144, end: 46.144, facts: 'Created by the test', changes: [46.144]},
  {name: 'Messaging consumer Default', start: 0.813, end: 1162.399, facts: 'Released at the end', changes: [0.813, 1162.399]},
];
const evidence = [
  {at: '+604 µs', kind: 'Moment', name: 'ASP.NET Core server · Northstar', operation: 'Initialize · Northstar (HttpClient)'},
  {at: '+1.0 ms', kind: 'Observation', name: 'scenario.started', operation: 'Before · NorthstarScenarioHook'},
  {at: '+1.15 s', kind: 'File', name: '597539000012-rest-01-response', operation: 'REST · GET /api/v1/organization'},
  {at: '+1.15 s', kind: 'Observation', name: 'http.response, GET /api/v1/organization', operation: 'REST · GET /api/v1/organization'},
  {at: '+1.15 s', kind: 'File', name: '597539000012-rest-01-expected-shape', operation: 'Assert response shape'},
];

function Ruler(): ReactNode {
  return <div className={styles.mockRuler} aria-label="Test clock, 0 to 1.16 seconds"><span>0</span><span>400 ms</span><span>800 ms</span><span>1.16 s</span></div>;
}

export default function TestViews({view}: {view: 'timeline' | 'state' | 'evidence'}): ReactNode {
  return (
    <div className={styles.testExcerpt}>
      <p className={styles.crumb}>Run / 12 A real wait does not close the due window</p>
      {view === 'timeline' && <>
        <p className={styles.panelLead}>Operation excerpt. The full Timeline is searchable, zooms per phase and dims framework work by default.</p>
        <Ruler />
        {operations.map(operation => <div key={operation.name} className={`${styles.clockRow} ${operation.failed ? styles.failed : ''}`}>
          <span>{operation.name}</span>
          <span className={styles.clockTrack}><i className={operation.gap ? styles.gapBar : undefined} style={{...place(operation.start, operation.length), ...(operation.gap ? {} : {background: `var(--phase-${operation.phase})`})}} /></span>
        </div>)}
      </>}
      {view === 'state' && <>
        <p className={styles.panelLead}>Selected operation: <strong>invoice.issue</strong>, +135 ms. Its time is shaded; the invoice it created is highlighted. Three tracked items are shown here.</p>
        <Ruler />
        {items.map(item => <div key={item.name} className={`${styles.clockRow} ${item.selected ? styles.itemSelected : ''}`}>
          <span><strong>{item.name}</strong><small>{item.facts}</small></span>
          <span className={styles.clockTrack}>
            <i className={styles.timeSelected} style={place(134.68, 0.006)} />
            <i className={styles.mockLife} style={place(item.start, item.end - item.start)} />
            {item.changes.map(at => <b key={at} className={styles.stateTick} style={{left: `${at / duration * 100}%`}} />)}
          </span>
        </div>)}
        <p className={styles.panelLead}>Created by invoice.issue, reported by the application. Select a tick in the viewer to open its operation.</p>
      </>}
      {view === 'evidence' && <>
        <p className={styles.panelLead}>Evidence excerpt in time order. Files, observations and moments keep their recording operation; findings appear here when recorded.</p>
        {evidence.map(entry => <div key={`${entry.kind}-${entry.name}`} className={styles.evidenceRow}>
          <span className={styles.archive}>{entry.at}</span><span className={styles.chip}>{entry.kind}</span>
          <strong>{entry.name}</strong><small>{entry.operation}</small>
        </div>)}
      </>}
    </div>
  );
}
