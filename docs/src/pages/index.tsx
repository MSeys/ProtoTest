import {useId, useRef, useState, type CSSProperties, type KeyboardEvent, type ReactNode} from 'react';
import Link from '@docusaurus/Link';
import Head from '@docusaurus/Head';
import Layout from '@theme/Layout';
import Heading from '@theme/Heading';
import CommandBox, {type CommandBoxRunner} from '@site/src/components/CommandBox';
import CodeSnippet from '@site/src/components/CodeSnippet';
import CopyCode from '@site/src/components/CopyCode';
import Frame from '@site/src/components/Frame';
import ReleaseFeed from '@site/src/components/ReleaseFeed';
import styles from './index.module.css';

interface TraceRowData {
  chip: string;
  tone: string;
  title: string;
  detail?: string;
}
const platformJourney = `[ProtoTest]
[SignedInAs]
public async Task RestWritesAreVisibleThroughGraphQL()
{
    using var created = await Proto.Context.Rest()
        .Body(new CreateProjectRequest("atlas"))
        .PostAsync("/api/v1/projects");
    created.Should.HaveHttpStatus(HttpStatusCode.Created);
    using var projects = await Proto.Context.GraphQL()
        .Query("projects", new { first = 10 })
        .ExpectAsync(new
        {
            totalCount = 1,
            nodes = new[] { new { name = "atlas", status = ProjectStatuses.Active } }
        });
    projects.Should.HaveNoErrors();
}`;
const journeyRows: TraceRowData[] = [
  {chip: 'Call', tone: '--type-call', title: 'REST · POST /api/v1/projects', detail: '201 Created'},
  {chip: 'Check', tone: '--type-assertion', title: 'Assert status · 201 Created', detail: 'matched'},
  {chip: 'Call', tone: '--type-call', title: 'GraphQL · query Projects', detail: '1 node'},
  {chip: 'Check', tone: '--type-assertion', title: 'Assert GraphQL data shape', detail: '1 node'},
  {chip: 'Cleanup', tone: '--type-ownership', title: 'Cleanup · TenantResponse', detail: 'tenant released'},
];
// Excerpt from ConfigureApplications in the same Northstar suite as PlatformJourney.
const composition = `app.AddRest(rest => rest
        .CaptureAttachments()
        .AddClient(NorthstarTargets.Api)
        .AddCollector<RestCoverageCollector>()
        .AddCollector<RestTrafficCoverageCollector>())
    .AddGraphQL(graphQL => graphQL
        .CaptureAttachments()
        .AddClient("GraphQL", endpoint: "GraphQL"));`;
const views = ['Compose', 'Test', 'Run'] as const;
function JourneyExample(): ReactNode {
  const id = useId();
  const [active, setActive] = useState(0);
  const buttons = useRef<(HTMLButtonElement | null)[]>([]);
  const code = active === 0 ? composition : platformJourney;
  function navigate(event: KeyboardEvent<HTMLButtonElement>, index: number): void {
    const next =
      event.key === 'ArrowRight'
        ? (index + 1) % views.length
        : event.key === 'ArrowLeft'
          ? (index + views.length - 1) % views.length
          : event.key === 'Home'
            ? 0
            : event.key === 'End'
              ? views.length - 1
              : null;
    if (next === null) return;
    event.preventDefault();
    setActive(next);
    buttons.current[next]?.focus();
  }
  return (
    <Frame
      kind="figure"
      head={
        <>
          <div role="tablist" aria-label="One Northstar scenario" className={styles.tabs}>
            {views.map((view, index) => (
              <button
                key={view}
                type="button"
                role="tab"
                id={`${id}-tab-${index}`}
                aria-controls={`${id}-panel`}
                aria-selected={active === index}
                tabIndex={active === index ? 0 : -1}
                ref={(button) => {
                  buttons.current[index] = button;
                }}
                onClick={() => setActive(index)}
                onKeyDown={(event) => navigate(event, index)}
              >
                {view}
              </button>
            ))}
          </div>
          {active < 2 && <CopyCode text={code} />}
        </>
      }
      foot={
        active === 0
          ? 'Setup.cs · ConfigureApplications excerpt. The suite registers its application, domain data and authentication separately.'
          : active === 1
            ? 'PlatformJourney.cs · RestWritesAreVisibleThroughGraphQL. The class selects the application and provisions a Northstar member.'
            : 'Selected records from test 18 in the committed prototest-demo.prototrace archive.'
      }
    >
      <div role="tabpanel" id={`${id}-panel`} aria-labelledby={`${id}-tab-${active}`}>
        {active < 2 ? (
          <CodeSnippet
            code={code}
            regionLabel={active === 0 ? 'Northstar composition' : 'Northstar journey'}
            scroll
          />
        ) : (
          <ol className={styles.traceRows}>
            {journeyRows.map((row) => (
              <li key={row.title}>
                <span
                  className={styles.traceKind}
                  style={{'--node-color': `var(${row.tone})`} as CSSProperties}
                >
                  {row.chip}
                </span>
                <strong>{row.title}</strong>
                <span>{row.detail}</span>
              </li>
            ))}
          </ol>
        )}
      </div>
    </Frame>
  );
}
function templateCommands(runner?: string): string[] {
  return [
    'dotnet new install ProtoTest.Templates',
    `dotnet new prototest -n Shop${runner ? ` --runner ${runner}` : ''}`,
    'cd Shop',
    'dotnet test',
  ];
}
const runnerChoices: CommandBoxRunner[] = [
  {id: 'nunit', label: 'NUnit (default)', commands: templateCommands()},
  {id: 'xunit', label: 'xUnit v2', commands: templateCommands('xunit')},
  {id: 'xunit3', label: 'xUnit v3', commands: templateCommands('xunit3')},
  {id: 'tunit', label: 'TUnit', commands: templateCommands('tunit')},
  {id: 'mstest', label: 'MSTest', commands: templateCommands('mstest')},
];
const integrations = [
  {
    title: 'Exercise your APIs',
    body: 'REST, GraphQL and gRPC clients with shared authentication, shape checks and contract coverage.',
    status: 'Supported',
    to: '/docs/integrations/rest',
  },
  {
    title: 'Arrange data and inspect the store',
    body: 'Reusable test data, test-scoped SQL connections and EF Core contexts, with optional transaction rollback.',
    status: 'Supported',
    to: '/docs/integrations/sql',
  },
  {
    title: 'Follow a journey in the browser',
    body: 'Page objects and flows through Playwright or Selenium, with screenshots and page coverage.',
    status: 'Supported',
    to: '/docs/integrations/web',
  },
  {
    title: 'Publish and await messages',
    body: 'In-memory messaging or RabbitMQ. Add the MassTransit bridge when you need its test harness.',
    status: 'Supported · MassTransit preview',
    to: '/docs/integrations/messaging',
  },
  {
    title: 'Compose the environment',
    body: 'ASP.NET Core, background workers and run-owned containers. Aspire and WireMock are available in preview.',
    status: 'Supported · preview extensions',
    to: '/docs/foundation/infrastructure',
  },
  {
    title: 'Reach devices and documents',
    body: 'Typed device clients over WebSocket or MQTT, and assertions on generated Excel workbooks.',
    status: 'Preview',
    to: '/docs/integrations/overview',
  },
];
const description =
  'ProtoTest is an integration testing foundation for .NET: compose applications, data and clients in one shared lifecycle, with a context and evidence trace for each test.';
export default function Home(): ReactNode {
  return (
    <Layout title="An integration testing foundation for .NET" description={description}>
      <Head>
        <script type="application/ld+json">
          {JSON.stringify({
            '@context': 'https://schema.org',
            '@type': 'SoftwareApplication',
            name: 'ProtoTest',
            applicationCategory: 'DeveloperApplication',
            operatingSystem: 'Windows, Linux, macOS',
            programmingLanguage: 'C#',
            url: 'https://prototest.dev/',
            downloadUrl: 'https://www.nuget.org/profiles/MSeys',
            codeRepository: 'https://github.com/MSeys/ProtoTest',
            license: 'https://github.com/MSeys/ProtoTest/blob/main/LICENSE',
            description,
          })}
        </script>
      </Head>
      <header className={styles.hero}>
        <div className={`container ${styles.heroGrid}`}>
          <div>
            <p className={styles.eyebrow}>ProtoTest for .NET</p>
            <Heading as="h1">
              A foundation for <span>integration testing.</span>
            </Heading>
            <p className={styles.lead}>
              Compose your application, test data and clients in one shared lifecycle. Write scenarios across
              APIs, databases, browsers and messages, with a trace that explains the run.
            </p>
            <div className={styles.actions}>
              <Link
                className={styles.primary}
                to="/docs/getting-started/installation#start-from-the-template"
              >
                Get started
              </Link>
              <Link className={styles.secondary} to="#one-scenario">
                See how it works <span aria-hidden="true">↓</span>
              </Link>
            </div>
            <p className={styles.compatibility}>.NET 8, 9 &amp; 10 · NUnit, xUnit, MSTest &amp; TUnit</p>
          </div>
          <div className={styles.foundation} aria-label="One host shared by tests, each with its own context">
            <div className={styles.host}>
              <span className={styles.eyebrow}>Suite foundation</span>
              <strong>One shared host</strong>
              <span>Application · clients · infrastructure</span>
            </div>
            <div className={styles.contexts}>
              {['Test A', 'Test B'].map((test) => (
                <div key={test}>
                  <strong>{test}</strong>
                  <code>Proto.Context</code>
                  <span>Data → scenario → cleanup</span>
                </div>
              ))}
            </div>
            <div className={styles.evidence}>
              <span className={styles.evidenceMark} aria-hidden="true">
                ◇
              </span>
              <span>Every test leaves its own evidence</span>
              <code>.prototrace</code>
            </div>
          </div>
        </div>
      </header>
      <main className={styles.main}>
        <section className={`container ${styles.section}`} aria-labelledby="foundation">
          <div className={styles.sectionHead}>
            <p className={styles.eyebrow}>The shared foundation</p>
            <Heading as="h2" id="foundation">
              Compose once. Give every test its own context.
            </Heading>
            <p>Your runner executes the tests. ProtoTest connects the pieces around them.</p>
          </div>
          <ol className={styles.steps}>
            <li>
              <span>01</span>
              <Heading as="h3">Compose the host</Heading>
              <p>Register applications, clients and infrastructure once for the suite.</p>
              <Link to="/docs/foundation/lifecycle#building-the-host">Host composition →</Link>
            </li>
            <li>
              <span>02</span>
              <Heading as="h3">Arrange the context</Heading>
              <p>Attributes and builders prepare identity, test data and the clients the scenario needs.</p>
              <Link to="/docs/foundation/execution-context">Execution context →</Link>
            </li>
            <li>
              <span>03</span>
              <Heading as="h3">Own the lifecycle</Heading>
              <p>Setup and teardown coordinate hooks and explicitly owned resources.</p>
              <Link to="/docs/foundation/lifecycle">Lifecycle and cleanup →</Link>
            </li>
            <li>
              <span>04</span>
              <Heading as="h3">Keep the evidence</Heading>
              <p>Calls, assertions and cleanup land in the same test’s trace and report.</p>
              <Link to="/docs/observability/prototrace">Trace and reports →</Link>
            </li>
          </ol>
        </section>
        <section className={`container ${styles.section}`} aria-labelledby="one-scenario">
          <div className={styles.sectionHead}>
            <p className={styles.eyebrow}>One scenario, end to end</p>
            <Heading as="h2" id="one-scenario">
              Write through REST. Read back through GraphQL.
            </Heading>
            <p>
              Follow the same Northstar journey from its host composition to the test and the recorded run.
            </p>
          </div>
          <JourneyExample />
          <p className={styles.note}>
            Read the{' '}
            <Link href="https://github.com/MSeys/ProtoTest/tree/main/samples/Northstar.ProtoTest">
              Northstar sample
            </Link>
            , or explore the recorded run in the{' '}
            <Link href="https://trace.prototest.dev/?demo=1">trace viewer</Link>.
          </p>
        </section>
        <section className={`container ${styles.section}`} aria-labelledby="integrations">
          <div className={styles.sectionHead}>
            <p className={styles.eyebrow}>Compose what you need</p>
            <Heading as="h2" id="integrations">
              Different boundaries. The same testing model.
            </Heading>
            <p>
              Choose integrations around the work your test does. Each joins the host, context and evidence
              pipeline.
            </p>
          </div>
          <div className={styles.cards}>
            {integrations.map((item) => (
              <Link key={item.title} to={item.to} className={styles.card}>
                <span className={styles.status}>{item.status}</span>
                <Heading as="h3">{item.title}</Heading>
                <p>{item.body}</p>
                <span className={styles.cardLink}>
                  Explore <span aria-hidden="true">→</span>
                </span>
              </Link>
            ))}
          </div>
          <p className={styles.note}>
            <Link to="/docs/integrations/overview">Browse the full integration catalog →</Link> ·{' '}
            <Link to="/docs/advanced/extending">Build an integration for your own domain</Link>
          </p>
        </section>
        <section className={styles.band} aria-labelledby="fit">
          <div className={`container ${styles.section}`}>
            <div className={styles.sectionHead}>
              <p className={styles.eyebrow}>Fit and evidence</p>
              <Heading as="h2" id="fit">
                Build on the tests you already have.
              </Heading>
            </div>
            <div className={styles.fitGrid}>
              <div>
                <p>
                  Keep your runner and assertions. Add a setup class and the integrations you need, then adopt
                  the shared lifecycle one scenario at a time.
                </p>
                <p>
                  <Link to="/docs/project/compare">Compare the setup</Link> ·{' '}
                  <Link to="/docs/runners/overview">Choose your runner</Link>
                </p>
                <p className={styles.note}>
                  MIT licensed. <Link to="/docs/project/sustainability">Project and support</Link> ·{' '}
                  <Link to="/docs/project/leaving">Adoption and exit costs</Link>
                </p>
              </div>
              <div className={styles.measurement}>
                <strong>35–36 ms</strong>
                <p>Per-test median in the 1,000-test OpenCSMS benchmark on the same machine.</p>
                <Link to="/docs/project/benchmarks#opencsms-at-1000-tests">
                  Read the measurements and their limits →
                </Link>
              </div>
            </div>
          </div>
        </section>
        <section className={`container ${styles.section} ${styles.try}`} aria-labelledby="try">
          <div>
            <p className={styles.eyebrow}>Try it</p>
            <Heading as="h2" id="try">
              Your first suite, ready to run.
            </Heading>
            <p>
              The template creates a small API and four integration tests. The .NET 10 SDK is enough for the
              default starter; it runs without containers or a browser.
            </p>
            <p>
              After the green run, find the trace and HTML report under{' '}
              <code>Shop.Tests/bin/Debug/net10.0/TestResults/</code>.
            </p>
            <p className={styles.note}>
              <Link to="/docs/getting-started/installation">Installation and framework options</Link>
              <br />
              <Link to="/learn/">Learn with the Northstar sample</Link> ·{' '}
              <Link to="/docs/recipes/overview">Find a recipe for your scenario</Link>
            </p>
          </div>
          <CommandBox title="Create and run Shop" commands={templateCommands()} runners={runnerChoices} />
        </section>
        <ReleaseFeed />
      </main>
    </Layout>
  );
}
