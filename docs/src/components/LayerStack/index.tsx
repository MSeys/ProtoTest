import {useState, type ReactNode} from 'react';
import Link from '@docusaurus/Link';

import styles from './styles.module.css';

interface Layer {
  id: string;
  index: string;
  label: string;
  status: 'shipped' | 'planned';
  packages: string[];
  body: string;
  to?: string;
}

const layers: Layer[] = [
  {
    id: 'interface',
    index: '01',
    label: 'Interface',
    status: 'shipped',
    packages: ['ProtoTest.Web', 'ProtoTest.Web.Playwright', 'ProtoTest.Web.Selenium'],
    body: 'Drive the browser through one page-object model — WebPage, WebElement, WebTable — backed by either Playwright or Selenium underneath.',
    to: '/docs/integrations/web',
  },
  {
    id: 'api',
    index: '02',
    label: 'API boundary',
    status: 'shipped',
    packages: ['ProtoTest.Rest', 'ProtoTest.GraphQL'],
    body: 'Call REST and GraphQL APIs through the same fluent client, response-shape assertions and shared authentication model. gRPC is next on this layer.',
    to: '/docs/integrations/rest',
  },
  {
    id: 'contracts',
    index: '03',
    label: 'Contracts',
    status: 'shipped',
    packages: ['ProtoTest.OpenApi'],
    body: 'Drive contract testing straight from an OpenAPI document instead of hand-written request builders.',
    to: '/docs/integrations/openapi',
  },
  {
    id: 'application',
    index: '04',
    label: 'Application',
    status: 'shipped',
    packages: ['ProtoTest.AspNetCore'],
    body: 'Test an ASP.NET Core application in-process, without a deployed host in between.',
    to: '/docs/integrations/aspnetcore',
  },
  {
    id: 'data',
    index: '05',
    label: 'Data',
    status: 'shipped',
    packages: ['ProtoTest.Data'],
    body: 'Build and provision test data with overridable defaults, instead of hand-writing object graphs per test.',
    to: '/docs/integrations/data',
  },
  {
    id: 'files',
    index: '06',
    label: 'Files & documents',
    status: 'planned',
    packages: [],
    body: 'Assert on documents your application generates — starting with spreadsheets (e.g. Excel workbooks built with SpreadsheetGear).',
  },
  {
    id: 'messaging',
    index: '07',
    label: 'Messaging',
    status: 'planned',
    packages: [],
    body: 'Assert on messages published to and consumed from a broker, such as RabbitMQ.',
  },
  {
    id: 'infrastructure',
    index: '08',
    label: 'Infrastructure',
    status: 'planned',
    packages: [],
    body: 'Spin up and tear down real dependencies for a run using Testcontainers, with the same lifecycle as everything else.',
  },
];

export default function LayerStack(): ReactNode {
  const [activeId, setActiveId] = useState(layers[0].id);
  const active = layers.find((layer) => layer.id === activeId)!;

  return (
    <div className={styles.stack}>
      <div className={styles.wrapper}>
        <div className={styles.rail}>
          <div className={styles.railLine} />
          {layers.map((layer) => (
            <button
              key={layer.id}
              type="button"
              className={`${styles.node} ${layer.id === activeId ? styles.nodeActive : ''}`}
              onClick={() => setActiveId(layer.id)}
              aria-pressed={layer.id === activeId}>
              <span className={`${styles.dot} ${layer.status === 'planned' ? styles.dotPlanned : ''}`} />
              <span className={styles.nodeIndex}>{layer.index}</span>
              <span className={styles.nodeLabel}>{layer.label}</span>
              {layer.status === 'planned' && <span className={styles.plannedTag}>Planned</span>}
            </button>
          ))}
        </div>

        <div className={styles.detail}>
          <div className={styles.detailKicker}>
            {active.status === 'shipped' ? 'Available today' : 'On the roadmap'}
          </div>
          <h3 className={styles.detailTitle}>{active.label}</h3>
          <p className={styles.detailBody}>{active.body}</p>
          {active.packages.length > 0 && (
            <div className={styles.packageRow}>
              {active.packages.map((pkg) => (
                <code key={pkg} className={styles.packageChip}>
                  {pkg}
                </code>
              ))}
            </div>
          )}
          {active.to && (
            <Link className={styles.detailLink} to={active.to}>
              Read the docs →
            </Link>
          )}
        </div>
      </div>
    </div>
  );
}
