import type {ReactNode} from 'react';
import Link from '@docusaurus/Link';

import Frame from '@site/src/components/Frame';
import {lessonTraces, type TraceSource} from '@site/src/data/traceSources';
import styles from './styles.module.css';

interface Entry {
  kind: string;
  name: string;
  meta?: string;
}

interface Layer {
  id: string;
  label: string;
  when: string;
  lead: string;
  entries: Entry[];
}

interface BlindSpot {
  title: string;
  body: string;
}

/*
 * One test's trace, read layer by layer. Names, numbers and messages are the recording's own: the test
 * is FailureDrills.TheTestClockClosesTheDueWindow in samples/Northstar.ProtoTest, recorded for
 * docs/static/lessons/l0-time-fix.prototrace. The last panel says what the same trace cannot show,
 * because that gap is the part a reader would otherwise misread.
 */

const defaultLayers: Layer[] = [
  {
    id: 'run',
    label: 'Run',
    when: 'before the first test',
    lead: 'What the run composed, and where the application ran. This is what the viewer shows on the run screen.',
    entries: [
      {
        kind: 'capability',
        name: 'REST, GraphQL, ASP.NET Core, SQL, Data, Sheets, Playwright',
        meta: 'the capabilities the composition declared',
      },
      {
        kind: 'server',
        name: 'ProtoTest.SampleApp.Program',
        meta: 'in-process, lifetime PerRun',
      },
      {
        kind: 'application',
        name: 'Northstar web on a loopback listener',
        meta: 'readiness /health, 1 attempt, waited 84 ms',
      },
      { kind: 'environment', name: '.NET 8.0.31 on Windows 10.0.26200, X64' },
    ],
  },
  {
    id: 'setup',
    label: 'Setup',
    when: '508.4 ms',
    lead: 'Everything before the test body: hooks, attributes, clients, the database connection and the state the test asked for.',
    entries: [
      { kind: 'test.setup', name: 'Setup', meta: '508.4 ms' },
      {
        kind: 'client.initialize',
        name: 'Rest, GraphQL, and the web client',
        meta: 'each client initializes once, with its address recorded',
      },
      { kind: 'sql.connection.open', name: 'Open SqliteConnection', meta: '0.05 ms' },
      {
        kind: 'attribute.before',
        name: 'Application, NorthstarTenant',
        meta: 'the tenant attribute provisions a TenantResponse in 151.7 ms',
      },
      { kind: 'attribute.before', name: 'SignedInAs, NorthstarMember', meta: 'the sign-in is recorded as state, not as a log line' },
    ],
  },
  {
    id: 'execution',
    label: 'Execution',
    when: '286.8 ms',
    lead: 'The test body. New entries nest under test.execution; the clock move is an event on it.',
    entries: [
      { kind: 'test.execution', name: 'Test execution', meta: '286.8 ms, succeeded' },
      { kind: 'data.create', name: 'Create · IssueInvoiceRequest', meta: '165.8 ms' },
      { kind: 'data.provision', name: 'Provision · IssueInvoiceRequest to InvoiceResponse', meta: '164.5 ms' },
      { kind: 'clock.advance', name: 'Clock advanced by 8:0:00:00', meta: 'event on test.execution, from the test side' },
      { kind: 'Northstar.Domain', name: 'invoice.issue', meta: 'reported by the application itself' },
      { kind: 'http.request', name: 'REST · GET /api/v1/organization', meta: '65.3 ms' },
      { kind: 'assert.http.status', name: 'Assert status · 200 OK', meta: 'the check that decided the request' },
      { kind: 'assert.json.shape', name: 'Assert response shape', meta: '7.5 ms, the property the test depended on' },
      { kind: 'http.request', name: 'REST · POST /api/v1/invoices/{invoiceId}/pay', meta: '35.0 ms' },
      { kind: 'assert.json.shape', name: 'Assert response shape', meta: 'the paid status' },
    ],
  },
  {
    id: 'teardown',
    label: 'Teardown',
    when: '40.6 ms',
    lead: 'What the test leaves behind, and what the run releases for it. The trace records the releases, so a leaked resource would show here.',
    entries: [
      { kind: 'test.teardown', name: 'Teardown', meta: '40.6 ms' },
      {
        kind: 'attachment.publish',
        name: '5 REST artifacts and the scenario summary',
        meta: 'request, response and expected shape for each call',
      },
      { kind: 'data.cleanup', name: 'Cleanup · TenantResponse', meta: '8.9 ms, the provisioned tenant is removed' },
      {
        kind: 'resource.release',
        name: 'Application services, database connection, messaging consumer',
        meta: 'released in order, each with its own release time',
      },
    ],
  },
];

const blindSpots: BlindSpot[] = [
  {
    title: 'Work outside the composition',
    body: 'The environment drill used a raw HttpClient against a fixed address. Its test.execution span ran 2.05 s and recorded no operation at all; the entry holds the connection failure, and the call it never wrapped cannot appear in the trace.',
  },
  {
    title: 'Work inside the application',
    body: 'invoice.issue and invoice.pay are there because the demo application reports that activity source. A step the application does not report has no span, however much it did.',
  },
  {
    title: 'Wall time',
    body: 'The clock item records the delta and the moment the clock stands at. It cannot tell you how long anything would have taken on the real clock.',
  },
];

interface TraceAnatomyProps {
  /** The run the walk reads. The default is the clock journey's own archive. */
  source?: TraceSource;
  /** The panel title. The default names the learn walk. */
  title?: ReactNode;
  /** The test the walk reads, printed under the title. */
  test?: string;
  /** The layers to render. The default is the clock journey's own four layers. */
  layers?: Layer[];
  /** The closing panel. Pass an empty array to leave it out. */
  blindSpots?: BlindSpot[];
}

export default function TraceAnatomy({
  source = lessonTraces.timeFix,
  title = 'One test, layer by layer',
  test = 'FailureDrills.TheTestClockClosesTheDueWindow',
  layers: propLayers,
  blindSpots: propBlindSpots,
}: TraceAnatomyProps): ReactNode {
  const layers = propLayers ?? defaultLayers;
  const spots = propBlindSpots ?? blindSpots;
  return (
    <Frame
      head={
        <>
          <strong>{title}</strong>
          <span className={styles.headMeta}>{test}</span>
        </>
      }
      foot={
        <>
          Read from {source.what}
          {source.file && <> in <code>{source.file}</code></>}. The viewer draws the same trace from
          that archive
          {source.href && <> (<Link href={source.href}>download it</Link> and drop it on the viewer)</>}.
        </>
      }>
      <ol className={styles.layers}>
        {layers.map((layer, index) => (
          <li key={layer.id} className={styles.layer}>
            <div className={styles.layerHead}>
              <span className={styles.number}>{String(index + 1).padStart(2, '0')}</span>
              <strong>{layer.label}</strong>
              <span className={styles.when}>{layer.when}</span>
            </div>
            <div className={styles.layerBody}>
              <p className={styles.lead}>{layer.lead}</p>
              <ul className={styles.entries}>
                {layer.entries.map((entry, entryIndex) => (
                  <li key={entryIndex} className={styles.entry}>
                    <code className={styles.kind}>{entry.kind}</code>
                    <span className={styles.name}>{entry.name}</span>
                    {entry.meta && <span className={styles.meta}>{entry.meta}</span>}
                  </li>
                ))}
              </ul>
            </div>
          </li>
        ))}
      </ol>

      {spots.length > 0 && (
      <section className={styles.blind} aria-labelledby="trace-anatomy-blind">
        <div className={styles.blindHead}>
          <h3 id="trace-anatomy-blind">What this trace cannot see</h3>
          <span className={styles.sources}>
            <span className={styles.sourceOn}>Test side</span>
            <span className={styles.sourceOff}>Observed</span>
            <span className={styles.sourceOn}>Application</span>
          </span>
        </div>
        <p className={styles.blindLead}>
          The run recorded values from the test side and from the application itself. Nothing was recorded
          as observed in a response, so the trace does not claim to have seen a value the test did not
          read. These are the edges of that picture.
        </p>
        <ul className={styles.blindList}>
          {spots.map((spot) => (
            <li key={spot.title}>
              <strong>{spot.title}</strong>
              <p>{spot.body}</p>
            </li>
          ))}
        </ul>
      </section>
      )}
    </Frame>
  );
}
