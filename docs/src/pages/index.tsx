import type {CSSProperties, ReactNode} from 'react';
import Link from '@docusaurus/Link';
import Head from '@docusaurus/Head';
import Layout from '@theme/Layout';
import Heading from '@theme/Heading';

import CommandBox, {type CommandBoxRunner} from '@site/src/components/CommandBox';
import CodeSnippet from '@site/src/components/CodeSnippet';
import Frame from '@site/src/components/Frame';
import styles from './index.module.css';

/*
 * The home page leads with the prototype idea, then shows the three commands, the test, the
 * machinery, the protocols, one failure, the measured proof and the entry points. Every trace row,
 * test line and number is quoted from the committed demo trace, the Northstar sample and the
 * benchmarks page.
 */

/* The trace rows the page shows: a viewer row is a kind chip, a title, and the detail the run recorded. */
interface TraceRowData {
  /** The kind chip's label, in the viewer's vocabulary. */
  chip: string;
  /** The execution-vocabulary token the chip is tinted with. */
  tone: string;
  title: string;
  detail?: string;
  /** A mono body line, for a request or response payload. */
  body?: string;
  status?: 'failed' | 'succeeded';
}

function tone(token: string): CSSProperties {
  return {'--node-color': `var(${token})`} as CSSProperties;
}

function TraceRows({rows}: {rows: TraceRowData[]}): ReactNode {
  return (
    <ul className={styles.traceRows}>
      {rows.map((row) => (
        <li key={row.title} className={styles.traceRow} data-status={row.status}>
          <span className={styles.traceKind} style={tone(row.tone)}>
            {row.chip}
          </span>
          <span className={styles.traceTitle}>{row.title}</span>
          {row.detail && <span className={styles.traceDetail}>{row.detail}</span>}
          {row.body && <code className={styles.traceBody}>{row.body}</code>}
        </li>
      ))}
    </ul>
  );
}

/*
 * Function heads read the demo's recorded rows. The check, the response body and the asserting line
 * are the committed prototest-demo.prototrace, test 08.
 */
const heroRows: TraceRowData[] = [
  {
    chip: 'Check',
    tone: '--type-assertion',
    title: 'Assert status · 201 Created',
    detail: 'Expected HTTP status 201 (Created), but received 400 (BadRequest).',
    status: 'failed',
  },
  {
    chip: 'Response',
    tone: '--type-artifact',
    title: 'POST /api/v1/projects returned 400 (BadRequest)',
    body: `{"code":"validation_failed","message":"The value cannot be an empty string or composed entirely of whitespace. (Parameter 'name')","details":null}`,
  },
  {
    chip: 'Source',
    tone: '--type-framework',
    title: 'FailureDrills.cs:139',
    detail: 'response.Should.HaveHttpStatus(HttpStatusCode.Created);',
  },
];

/* The real sample, samples/Northstar.ProtoTest/PlatformJourney.cs, one REST write read back over GraphQL. */
const platformJourney = `[Application(NorthstarTargets.Api)]
[NorthstarMember(PlanIds.Growth)]
public sealed class PlatformJourney
{
    [ProtoTest]
    [SignedInAs]
    public async Task RestWritesAreVisibleThroughGraphQL()
    {
        // Arrange: write through the public REST surface.
        using var created = await Proto.Context.Rest()
            .Body(new CreateProjectRequest("atlas"))
            .PostAsync("/api/v1/projects");
        created.Should.HaveHttpStatus(HttpStatusCode.Created);

        // Act
        using var projects = await Proto.Context.GraphQL()
            .Query("projects", new { first = 10 })
            .ExpectAsync(new
            {
                totalCount = 1,
                nodes = new[] { new { name = "atlas", status = ProjectStatuses.Active } }
            });

        // Assert
        projects.Should.HaveNoErrors();
    }
}`;

/* What that test recorded, from test 18 of the committed demo trace. */
const journeyRows: TraceRowData[] = [
  {chip: 'Call', tone: '--type-call', title: 'REST · POST /api/v1/projects', detail: '201 Created'},
  {chip: 'Call', tone: '--type-call', title: 'GraphQL · query Projects', detail: '1 node'},
  {chip: 'Check', tone: '--type-assertion', title: 'Assert status · 201 Created', detail: 'matched'},
  {chip: 'Check', tone: '--type-assertion', title: 'Assert GraphQL data shape', detail: '1 node'},
  {chip: 'Cleanup', tone: '--type-ownership', title: 'Cleanup · TenantResponse', detail: 'tenant released'},
];

/* The failure drill, test 08: what was sent, what came back, the state, the cleanup and the line. */
const failureRows: TraceRowData[] = [
  {chip: 'Call', tone: '--type-call', title: 'POST /api/v1/projects', body: '{"name":""}'},
  {
    chip: 'Response',
    tone: '--type-artifact',
    title: '400 BadRequest · validation_failed',
    body: `{"code":"validation_failed","message":"The value cannot be an empty string or composed entirely of whitespace. (Parameter 'name')","details":null}`,
  },
  {
    chip: 'State',
    tone: '--type-ownership',
    title: 'data:TenantResponse:1',
    detail: "Provisioned TenantResponse 'northstar-597539000008', created by the test.",
  },
  {
    chip: 'Cleanup',
    tone: '--type-ownership',
    title: 'Cleanup · TenantResponse',
    detail: 'northstar-597539000008 removed on teardown.',
  },
  {
    chip: 'Source',
    tone: '--type-framework',
    title: 'FailureDrills.cs:139',
    detail: 'response.Should.HaveHttpStatus(HttpStatusCode.Created);',
    status: 'failed',
  },
];

function templateCommands(runner?: string): string[] {
  return [
    'dotnet new install ProtoTest.Templates',
    `dotnet new prototest -n Shop${runner ? ` --runner ${runner}` : ''}`,
    'dotnet test',
  ];
}

// The template defaults to NUnit, so its row keeps the plain commands a reader copies without thinking.
const runnerChoices: CommandBoxRunner[] = [
  {id: 'nunit', label: 'NUnit (default)', commands: templateCommands()},
  {id: 'xunit', label: 'xUnit v2', commands: templateCommands('xunit')},
  {id: 'xunit3', label: 'xUnit v3', commands: templateCommands('xunit3')},
  {id: 'tunit', label: 'TUnit', commands: templateCommands('tunit')},
  {id: 'mstest', label: 'MSTest', commands: templateCommands('mstest')},
];

const machinery: {title: string; body: ReactNode; to: string}[] = [
  {
    title: 'The host, per suite',
    body: 'Application, clients, servers, databases and brokers are composed once, in Configure.',
    to: '/docs/foundation/overview',
  },
  {
    title: 'The context, per test',
    body: 'Clients, state and trace are scoped to one test, and the suite stays parallel-safe.',
    to: '/docs/foundation/execution-context',
  },
  {
    title: 'The lifecycle',
    body: 'Setup, authentication, cleanup and teardown run through attributes and hooks, not copy-paste.',
    to: '/docs/foundation/lifecycle',
  },
  {
    title: 'Your runner',
    body: (
      <>
        NUnit, xUnit.net v2, xUnit.net v3, TUnit and MSTest stay; add <code>[ProtoTest]</code>.
      </>
    ),
    to: '/docs/runners/overview',
  },
];

const protocols = [
  {
    title: 'REST, GraphQL and gRPC',
    packages: 'ProtoTest.Rest · ProtoTest.GraphQL · ProtoTest.Grpc',
    body: 'Fluent clients over one application, one auth model.',
    to: '/docs/integrations/rest',
  },
  {
    title: 'SQL and EF Core',
    packages: 'ProtoTest.Sql · ProtoTest.Sql.EntityFrameworkCore',
    body: 'A database per test, rolled back on teardown.',
    to: '/docs/integrations/sql',
  },
  {
    title: 'Browsers',
    packages: 'ProtoTest.Web.Playwright · ProtoTest.Web.Selenium',
    body: "Playwright or Selenium, sharing the suite's sign-in and trace.",
    to: '/docs/integrations/web',
  },
  {
    title: 'Messaging',
    packages: 'ProtoTest.Messaging.RabbitMq · ProtoTest.Messaging.MassTransit · ProtoTest.Devices.Mqtt',
    body: 'RabbitMQ, MassTransit and MQTT, with publish and await steps.',
    to: '/docs/integrations/messaging',
  },
  {
    title: 'Devices, Sheets and more',
    packages: 'ProtoTest.Devices · ProtoTest.Sheets · ProtoTest.WireMock · ProtoTest.Testcontainers · ProtoTest.Aspire',
    body: 'Chargers, spreadsheets, WireMock, Testcontainers, Aspire.',
    to: '/docs/integrations/overview',
  },
];

const proof = [
  {
    value: '35-36 ms',
    label: 'per-test median, the 1,000-test OpenCSMS benchmark, same machine',
    to: '/docs/project/benchmarks#opencsms-at-1000-tests',
  },
  {
    value: '1.3 s',
    label: 'viewer cold open, the 1,200-test run',
    to: '/docs/project/benchmarks#the-viewer-at-1000-tests',
  },
  {
    value: '44 packages',
    label: 'on NuGet for .NET 8, 9 and 10',
    to: '/docs/getting-started/installation',
  },
];

/*
 * First screen: the prototype claim, the two actions, and the real failure the trace explains. The
 * copy and the frame sit side by side and stack on their own width.
 */
function Hero(): ReactNode {
  return (
    <header data-surface="blueprint" className={styles.hero}>
      <div className="container">
        <div className={styles.heroSplit}>
          <div className={styles.heroCopy}>
            <div className={styles.eyebrow}>Integration testing for .NET</div>
            <Heading as="h1" className={styles.heroTitle}>
              Integration tests as readable as a prototype.
            </Heading>
            <p className={styles.heroLead}>
              ProtoTest is prototype testing for .NET: one host, one lifecycle and one trace across
              REST, GraphQL, gRPC, SQL, browsers and brokers. The setup lives in the host; the test
              stays the scenario.
            </p>
            <div className={styles.heroButtons}>
              <Link className={`${styles.btn} ${styles.btnPrimary}`} to="/docs/getting-started/first-test">
                Start in three commands
              </Link>
              <Link className={`${styles.btn} ${styles.btnGhost}`} href="https://trace.prototest.dev/?demo=1">
                Open a failing trace
              </Link>
            </div>
            <p className={styles.heroNote}>
              Requires the .NET SDK only. The starter suite is green on a fresh checkout, no
              containers or browsers needed.
            </p>
          </div>

          <Frame
            className={styles.heroFrame}
            head={
              <>
                <strong>prototest-demo.prototrace</strong>
                <span className={styles.frameMeta}>failure 08</span>
              </>
            }
            foot={
              <>
                FailureDrills.ABareStatusHidesWhatTheApplicationSaid, from the committed demo trace.{' '}
                <Link href="https://trace.prototest.dev/?demo=1">Open it in the viewer</Link>.
              </>
            }>
            <TraceRows rows={heroRows} />
          </Frame>
        </div>
      </div>
    </header>
  );
}

function CommandsSection(): ReactNode {
  return (
    <section className={styles.section}>
      <div className="container">
        <header className={styles.sectionHead}>
          <Heading as="h2">Three commands to a green suite.</Heading>
        </header>
        <div className={styles.commandsInner}>
          <CommandBox title="Start a suite" commands={templateCommands()} runners={runnerChoices} />
          <p className={styles.line}>
            The template writes a suite that runs as-is; each runner row shows its commands.{' '}
            <Link to="/docs/getting-started/installation">Installation</Link>
          </p>
        </div>
      </div>
    </section>
  );
}

function ScenarioSection(): ReactNode {
  return (
    <section className={styles.section}>
      <div className="container">
        <header className={styles.sectionHead}>
          <Heading as="h2">The test is the scenario.</Heading>
        </header>
        <div className={styles.split}>
          <Frame
            head={
              <>
                <strong>PlatformJourney.cs</strong>
                <span className={styles.frameMeta}>samples/Northstar.ProtoTest</span>
              </>
            }>
            <CodeSnippet code={platformJourney} language="csharp" showLineNumbers />
          </Frame>
          <Frame
            head={
              <>
                <strong>What the run recorded</strong>
                <span className={styles.frameMeta}>prototest-demo.prototrace</span>
              </>
            }
            foot="Test 18, RestWritesAreVisibleThroughGraphQL. Rows quoted from the committed demo trace.">
            <TraceRows rows={journeyRows} />
          </Frame>
        </div>
        <p className={styles.line}>
          The shared setup lives in the host composition; the attributes carry identity, auth and
          lifecycle; the method is the journey.{' '}
          <Link to="/docs/foundation/execution-context">Execution context</Link>{' '}
          <Link to="/docs/foundation/lifecycle">Lifecycle</Link>
        </p>
      </div>
    </section>
  );
}

function MachinerySection(): ReactNode {
  return (
    <section className={`${styles.section} ${styles.band}`}>
      <div className="container">
        <header className={styles.sectionHead}>
          <Heading as="h2">Where the machinery lives.</Heading>
        </header>
        <div className={styles.machinery}>
          {machinery.map((item) => (
            <Link key={item.title} className={styles.machineryItem} to={item.to}>
              <strong>{item.title}</strong>
              <span>{item.body}</span>
            </Link>
          ))}
        </div>
      </div>
    </section>
  );
}

function ProtocolsSection(): ReactNode {
  return (
    <section className={styles.section}>
      <div className="container">
        <header className={styles.sectionHead}>
          <Heading as="h2">Every protocol, one suite.</Heading>
        </header>
        <div className={styles.cards}>
          {protocols.map((card) => (
            <Link key={card.title} className={styles.card} to={card.to}>
              <strong>{card.title}</strong>
              <code className={styles.cardPackages}>{card.packages}</code>
              <span>{card.body}</span>
            </Link>
          ))}
        </div>
        <p className={styles.line}>
          44 packages, 29 supported and 15 preview; the integrations map marks which.{' '}
          <Link to="/docs/integrations/overview">Integrations map</Link>
        </p>
      </div>
    </section>
  );
}

function FailureSection(): ReactNode {
  return (
    <section className={styles.section}>
      <div className="container">
        <header className={styles.sectionHead}>
          <Heading as="h2">When it fails, the run explains itself.</Heading>
          <p className={styles.sectionLead}>Expected 201 Created, got 400 Bad Request.</p>
        </header>
        <div className={styles.failureInner}>
          <Frame
            head={
              <>
                <strong>FailureDrills.ABareStatusHidesWhatTheApplicationSaid</strong>
                <span className={styles.frameMeta}>prototest-demo.prototrace</span>
              </>
            }
            foot="One column, in run order, quoted from the committed demo trace.">
            <TraceRows rows={failureRows} />
          </Frame>
        </div>
        <p className={styles.line}>
          Setup, every call, the checks, the state it changed and the cleanup are in the .prototrace
          file. Read it in the{' '}
          <Link href="https://trace.prototest.dev/?demo=1">viewer</Link>, or through{' '}
          <strong>ProtoTest.Mcp</strong>: four read-only tools over the traces in a repository, no
          port opened, nothing uploaded.{' '}
          <Link to="/docs/observability/prototrace#a-walk-through-one-test">Trace anatomy</Link>{' '}
          <Link to="/docs/agent-workflows/setup">Agent setup</Link>
        </p>
      </div>
    </section>
  );
}

function ProofSection(): ReactNode {
  return (
    <section className={`${styles.section} ${styles.band}`}>
      <div className="container">
        <header className={styles.sectionHead}>
          <Heading as="h2">Proof, measured.</Heading>
        </header>
        <div className={styles.stats}>
          {proof.map((stat) => (
            <Link key={stat.value} className={styles.stat} to={stat.to}>
              <strong>{stat.value}</strong>
              <span>{stat.label}</span>
            </Link>
          ))}
        </div>
        <div className={styles.adoption}>
          <Heading as="h3">Already have a suite?</Heading>
          <p>
            The comparison prices the plumbing per fixture: 35 lines without ProtoTest, 13 with.
            Adoption is one setup class per project and one package per integration.{' '}
            <Link to="/docs/project/compare">Compare</Link>{' '}
            <Link to="/docs/getting-started/migrating-from-1-0">Migration cost</Link>
          </p>
        </div>
        <p className={styles.honesty}>
          ProtoTest is not a runner and not a mock library. Where it does not fit, the docs say so
          first. <Link to="/docs/project/leaving">Leaving ProtoTest</Link>
        </p>
      </div>
    </section>
  );
}

function Close(): ReactNode {
  return (
    <section className={`${styles.section} ${styles.close}`}>
      <div className="container">
        <header className={styles.sectionHead}>
          <Heading as="h2">Start where you are.</Heading>
        </header>
        <nav className={styles.closeNav} aria-label="Start where you are">
          <Link to="/docs/getting-started/first-test">Your first test</Link>
          <Link to="/learn/">Learn track</Link>
          <Link to="/docs/recipes/overview">Recipes</Link>
          <Link to="/docs/getting-started/troubleshooting">Troubleshooting</Link>
          <Link to="/docs/project/faq">Adoption questions</Link>
        </nav>
        <p className={styles.closeLine}>
          Three commands, a green suite, and a trace for the first time it fails.
        </p>
      </div>
    </section>
  );
}

export default function Home(): ReactNode {
  return (
    <Layout
      title="Integration tests as readable as a prototype."
      description="ProtoTest is prototype testing for .NET: one host, one lifecycle and one trace across REST, GraphQL, gRPC, SQL, browsers and brokers.">
      <Head>
        <script type="application/ld+json">
          {JSON.stringify({
            '@context': 'https://schema.org',
            '@type': 'SoftwareApplication',
            name: 'ProtoTest',
            applicationCategory: 'DeveloperApplication',
            operatingSystem: 'Windows, Linux, macOS',
            softwareVersion: '1.1.0',
            programmingLanguage: 'C#',
            url: 'https://prototest.dev/',
            downloadUrl: 'https://www.nuget.org/profiles/MSeys',
            codeRepository: 'https://github.com/MSeys/ProtoTest',
            license: 'https://github.com/MSeys/ProtoTest/blob/main/LICENSE',
            description:
              'ProtoTest is prototype testing for .NET: one host, one lifecycle and one trace across REST, GraphQL, gRPC, SQL, browsers and brokers.',
          })}
        </script>
      </Head>
      <Hero />
      <main>
        <CommandsSection />
        <ScenarioSection />
        <MachinerySection />
        <ProtocolsSection />
        <FailureSection />
        <ProofSection />
        <Close />
      </main>
    </Layout>
  );
}
