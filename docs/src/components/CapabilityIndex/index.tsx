import type {CSSProperties, ReactNode} from 'react';
import Link from '@docusaurus/Link';

import Frame from '@site/src/components/Frame';
import styles from './styles.module.css';

/*
 * The system under test in cross-section, from what a user sees down to what the run itself brings, with every
 * capability placed at the depth it reaches. The spine on the left is the one ProtoExecutionContext every depth
 * plugs into: a test that drives the browser and one that reads the database are written the same way.
 */

interface Capability {
  name: string;
  does: string;
  packages: string;
  to?: string;
}

interface Depth {
  label: string;
  note: string;
  capabilities: Capability[];
}

const depths: Depth[] = [
  {
    label: 'In front of it',
    note: 'What a user sees',
    capabilities: [
      {name: 'Browser', does: 'Page objects and named flows', packages: 'Web · Playwright · Selenium', to: '/docs/integrations/web'},
    ],
  },
  {
    label: 'At its boundary',
    note: 'What it exposes',
    capabilities: [
      {name: 'REST', does: 'One traced client per API', packages: 'Rest', to: '/docs/integrations/rest'},
      {name: 'GraphQL', does: 'Shapes as selections', packages: 'GraphQL', to: '/docs/integrations/graphql'},
      {name: 'gRPC', does: 'Calls and streams', packages: 'Grpc', to: '/docs/integrations/grpc'},
      {name: 'Contracts', does: 'Coverage of the API document', packages: 'OpenApi', to: '/docs/integrations/openapi'},
    ],
  },
  {
    label: 'Inside it',
    note: 'The application itself',
    capabilities: [
      {name: 'In-process host', does: 'No deployed environment', packages: 'AspNetCore', to: '/docs/integrations/aspnetcore'},
      {name: 'Its own telemetry', does: 'Values the app reports', packages: 'OpenTelemetry', to: '/docs/observability/opentelemetry'},
    ],
  },
  {
    label: 'Behind it',
    note: 'What it writes to',
    capabilities: [
      {name: 'Database', does: 'The store the app uses', packages: 'Sql · EntityFrameworkCore', to: '/docs/integrations/sql'},
      {name: 'Messaging', does: 'Publish, then await events', packages: 'Messaging · RabbitMq', to: '/docs/integrations/messaging'},
      {name: 'Documents', does: 'The workbooks it generates', packages: 'Sheets', to: '/docs/integrations/sheets'},
    ],
  },
  {
    label: 'Around the run',
    note: 'What the run brings',
    capabilities: [
      {name: 'Test data', does: 'Provisioned, with defaults', packages: 'Data', to: '/docs/integrations/data'},
      {name: 'Infrastructure', does: 'Containers owned by the run', packages: 'Testcontainers', to: '/docs/foundation/infrastructure'},
    ],
  },
];

function Card({capability}: {capability: Capability}): ReactNode {
  const body = (
    <>
      <strong>{capability.name}</strong>
      <span className={styles.does}>{capability.does}</span>
      <code className={styles.packages}>{capability.packages}</code>
    </>
  );
  return capability.to ? (
    <Link className={styles.card} to={capability.to}>
      {body}
    </Link>
  ) : (
    <div className={styles.card}>{body}</div>
  );
}

export default function CapabilityIndex(): ReactNode {
  const count = depths.reduce((sum, depth) => sum + depth.capabilities.length, 0);
  return (
    <Frame
      head={
        <>
          <code className={styles.context}>ProtoExecutionContext</code>
          <span className={styles.meta}>{count} capabilities, one context</span>
        </>
      }
      foot={<>Every package is ProtoTest.* — compose the ones your suite needs.</>}>
      <ol className={styles.depths}>
        {depths.map((depth, index) => (
          <li key={depth.label} className={styles.depth} style={{'--level': index} as CSSProperties}>
            <div className={styles.label}>
              <i className={styles.node} aria-hidden="true" />
              <strong>{depth.label}</strong>
              <span>{depth.note}</span>
            </div>
            <div className={styles.cards}>
              {depth.capabilities.map((capability) => (
                <Card key={capability.name} capability={capability} />
              ))}
            </div>
          </li>
        ))}
      </ol>
    </Frame>
  );
}
