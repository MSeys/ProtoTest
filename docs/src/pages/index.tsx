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
import ViewerMock from '@site/src/components/ViewerMock';
import {withoutProtoTest, withProtoTest} from '@site/src/data/comparison';
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
  {
    chip: 'Call',
    tone: '--type-call',
    title: 'REST · POST /api/v1/projects',
    detail: '201 Created',
  },
  {
    chip: 'Check',
    tone: '--type-assertion',
    title: 'Assert status · 201 Created',
    detail: 'matched',
  },
  {
    chip: 'Call',
    tone: '--type-call',
    title: 'GraphQL · query Projects',
    detail: '1 node',
  },
  {
    chip: 'Check',
    tone: '--type-assertion',
    title: 'Assert GraphQL data shape',
    detail: '1 node',
  },
  {
    chip: 'Cleanup',
    tone: '--type-ownership',
    title: 'Cleanup · TenantResponse',
    detail: 'tenant released',
  },
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
const views = ['Test', 'Compose', 'Trace'] as const;
function JourneyExample(): ReactNode {
  const id = useId();
  const [active, setActive] = useState(0);
  const buttons = useRef<(HTMLButtonElement | null)[]>([]);
  const code = active === 0 ? platformJourney : composition;
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
      kind="code"
      className={styles.heroFrame}
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
          ? 'PlatformJourney.cs. The class selects the application and provisions a Northstar member.'
          : active === 1
            ? 'Setup.cs. Registered once for the suite, next to its data and authentication.'
            : 'What this test left in prototest-demo.prototrace, as the viewer lists it.'
      }
    >
      <div role="tabpanel" id={`${id}-panel`} aria-labelledby={`${id}-tab-${active}`}>
        {active < 2 ? (
          <CodeSnippet
            code={code}
            regionLabel={active === 0 ? 'Northstar journey' : 'Northstar composition'}
            scroll
          />
        ) : (
          <ol className={styles.traceRows}>
            {journeyRows.map((row) => (
              <li key={row.title}>
                <span
                  className={styles.traceKind}
                  style={
                    {
                      '--node-color': `var(${row.tone})`,
                    } as CSSProperties
                  }
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
/** Plumbing lines in the fixture each side writes per test class, counted from the comparison's own files. */
function plumbing(files: typeof withProtoTest): number {
  return files.find((file) => file.scope === 'test')?.infrastructureLines.length ?? 0;
}
const plumbingWithout = plumbing(withoutProtoTest);
const plumbingWith = plumbing(withProtoTest);
const setupMoves = [
  ['Run the application', 'A factory per fixture', 'The suite host'],
  ['A tenant of its own', 'SetUp and a helper', 'An attribute'],
  ['Authenticate each call', 'Shared default headers', 'Per test, from its context'],
  ['Clean up after a failure', 'TearDown, if SetUp got far', 'Owned, reversed and traced'],
] as const;
const description =
  'ProtoTest is an integration testing foundation for .NET: compose applications, data and clients once, give every test its own context and cleanup, and keep a trace that explains the run.';
export default function Home(): ReactNode {
  return (
    <Layout title="Integration testing foundation for .NET" description={description}>
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
      <header data-surface="blueprint" className={styles.hero}>
        <div className={`container ${styles.heroGrid}`}>
          <div className={styles.heroCopy}>
            <p className={styles.eyebrow}>Integration testing foundation for .NET</p>
            <Heading as="h1">
              Test the whole journey. <span>Trace every layer.</span>
            </Heading>
            <p className={styles.lead}>
              Compose your application, data and clients once for the suite. Every test gets its own context,
              owned cleanup and a trace that explains what happened, across APIs, databases, browsers and
              messages.
            </p>
            <div className={styles.actions}>
              <Link
                className={styles.primary}
                to="/docs/getting-started/installation#start-from-the-template"
              >
                Get started
              </Link>
              <Link className={styles.secondary} href="https://trace.prototest.dev/?demo=1">
                Open a sample trace <span aria-hidden="true">↗</span>
              </Link>
            </div>
            <p className={styles.compatibility}>
              .NET 8, 9 &amp; 10 · NUnit, xUnit, MSTest &amp; TUnit · MIT
            </p>
          </div>
          <JourneyExample />
        </div>
      </header>
      <main className={styles.main}>
        <section className={`container ${styles.section}`} aria-labelledby="setup">
          <div className={styles.feature}>
            <div className={styles.featureCopy}>
              <p className={styles.eyebrow}>Less plumbing</p>
              <Heading as="h2" id="setup">
                Setup moves out of the test.
              </Heading>
              <p>
                The same scenario against the same application, written as a plain fixture and with ProtoTest.
                The scenario lines stay; the plumbing around them is composed once for the suite or declared
                on the test that needs it.
              </p>
              <Link className={styles.more} to="/docs/project/compare">
                Compare both files, line by line →
              </Link>
            </div>
            <figure className={styles.score}>
              <div className={styles.scoreHead}>
                <div>
                  <span>Without ProtoTest</span>
                  <strong>{plumbingWithout}</strong>
                </div>
                <div className={styles.scoreWith}>
                  <span>With ProtoTest</span>
                  <strong>{plumbingWith}</strong>
                </div>
              </div>
              <figcaption>lines of plumbing in each fixture, for the same scenario</figcaption>
              <dl className={styles.moves}>
                {setupMoves.map(([task, before, after]) => (
                  <div key={task}>
                    <dt>{task}</dt>
                    <dd>
                      <s>{before}</s>
                      <span>{after}</span>
                    </dd>
                  </div>
                ))}
              </dl>
            </figure>
          </div>
        </section>
        <section className={`container ${styles.section}`} aria-labelledby="failure">
          <div className={`${styles.feature} ${styles.featureReverse}`}>
            <div className={styles.featureCopy}>
              <p className={styles.eyebrow}>Evidence</p>
              <Heading as="h2" id="failure">
                A failure explains itself.
              </Heading>
              <p>
                Every call, check, wait and cleanup lands in a <code>.prototrace</code> archive next to the
                report. Open it and the failing check is one step away, with the values that differed and the
                source line that asserted them.
              </p>
              <p>
                The trace also shows time nothing was recorded, so a real wait is visible instead of guessed.
              </p>
              <div className={styles.links}>
                <Link className={styles.more} href="https://trace.prototest.dev/?demo=1">
                  Open this run in the viewer ↗
                </Link>
                <Link className={styles.more} to="/docs/observability/prototrace">
                  How the trace works →
                </Link>
              </div>
            </div>
            <Frame
              kind="preview"
              className={styles.checkFrame}
              head={<span className={styles.frameTitle}>ProtoTrace viewer · failing check</span>}
              foot="Test 12 from prototest-demo.prototrace, as the inspector shows it."
            >
              <ViewerMock screen="check" />
            </Frame>
          </div>
        </section>
        <section className={`container ${styles.section}`} aria-labelledby="integrations">
          <div className={styles.sectionHead}>
            <p className={styles.eyebrow}>One model</p>
            <Heading as="h2" id="integrations">
              Different boundaries. The same test.
            </Heading>
            <p>
              Choose the integrations your scenario crosses. Each joins the same host, context, cleanup and
              trace, so one test can write through an API and read back from the database or the browser.
            </p>
          </div>
          <div className={styles.cards}>
            {integrations.map((item) => (
              <Link key={item.title} to={item.to} className={styles.card}>
                <Heading as="h3">{item.title}</Heading>
                <p>{item.body}</p>
                <span className={styles.status}>{item.status}</span>
              </Link>
            ))}
          </div>
          <p className={styles.note}>
            <Link to="/docs/integrations/overview">Every integration →</Link> ·{' '}
            <Link to="/docs/recipes/overview">Recipes that combine them</Link> ·{' '}
            <Link to="/docs/advanced/extending">Build one for your own domain</Link>
          </p>
        </section>
        <section className={styles.band} aria-labelledby="foundation">
          <div className={`container ${styles.section}`}>
            <div className={styles.sectionHead}>
              <p className={styles.eyebrow}>How it fits</p>
              <Heading as="h2" id="foundation">
                Your runner runs the tests. ProtoTest holds what is around them.
              </Heading>
            </div>
            <ol className={styles.steps}>
              <li>
                <span>01</span>
                <Heading as="h3">Compose the host</Heading>
                <p>Applications, clients and infrastructure, registered once for the suite.</p>
                <Link to="/docs/foundation/lifecycle#building-the-host">Host composition →</Link>
              </li>
              <li>
                <span>02</span>
                <Heading as="h3">Arrange the context</Heading>
                <p>Attributes and builders prepare the identity, data and clients one test needs.</p>
                <Link to="/docs/foundation/execution-context">Execution context →</Link>
              </li>
              <li>
                <span>03</span>
                <Heading as="h3">Own the cleanup</Heading>
                <p>Whatever a test provisions is released in reverse order, also when it fails.</p>
                <Link to="/docs/foundation/lifecycle">Lifecycle and cleanup →</Link>
              </li>
              <li>
                <span>04</span>
                <Heading as="h3">Keep the evidence</Heading>
                <p>Calls, checks and cleanup land in the test’s trace, report and coverage.</p>
                <Link to="/docs/observability/prototrace">Trace and reports →</Link>
              </li>
            </ol>
            <div className={styles.fit}>
              <p>
                Keep your runner and your assertions. Add a setup class and the integrations you need, then
                move one scenario at a time. <Link to="/docs/project/compare">Compare the setup</Link> ·{' '}
                <Link to="/docs/runners/overview">Choose your runner</Link> ·{' '}
                <Link to="/docs/project/leaving">Adoption and exit costs</Link>
              </p>
              <Link className={styles.measurement} to="/docs/project/benchmarks#opencsms-at-1000-tests">
                <strong>{'< 1 ms'}</strong>
                <span>of a 36 ms test in a real suite with a database and a broker is ProtoTest. Where the rest goes →</span>
              </Link>
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
              The template creates a small API and four integration tests. The .NET 10 SDK is enough. The
              default starter runs without containers or a browser.
            </p>
            <p>
              After the green run, the trace and HTML report are under{' '}
              <code>Shop.Tests/bin/Debug/net10.0/TestResults/</code>.
            </p>
            <p className={styles.note}>
              <Link to="/docs/getting-started/installation">Installation and options</Link> ·{' '}
              <Link to="/learn/">Learn with the Northstar sample</Link>
            </p>
          </div>
          <CommandBox title="Create and run Shop" commands={templateCommands()} runners={runnerChoices} />
        </section>
        <section className={`container ${styles.origin}`} aria-labelledby="why">
          <div>
            <Heading as="h2" id="why">
              Why ProtoTest exists
            </Heading>
            <p>
              Once an integration suite grows, most of the work shifts to setup, infrastructure and failures
              that only happen sometimes. ProtoTest gives every integration the same context, lifecycle and
              trace, so that work is solved once. What is specific to your application stays in your test
              project. <Link to="/docs/project/sustainability">Project and support</Link>
            </p>
          </div>
        </section>
        <ReleaseFeed />
      </main>
    </Layout>
  );
}
