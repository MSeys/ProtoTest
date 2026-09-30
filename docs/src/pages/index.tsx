import type {ReactNode} from 'react';
import Link from '@docusaurus/Link';
import Head from '@docusaurus/Head';
import Layout from '@theme/Layout';
import Heading from '@theme/Heading';

import CommandBox, {type CommandBoxRunner} from '@site/src/components/CommandBox';
import CodeSnippet from '@site/src/components/CodeSnippet';
import Frame from '@site/src/components/Frame';
import TabbedCode, {type CodeTab} from '@site/src/components/TabbedCode';
import ViewerWalkthrough from '@site/src/components/ViewerWalkthrough';
import FailureGallery from '@site/src/components/FailureGallery';
import styles from './index.module.css';

const heroTabs: CodeTab[] = [
  {
    id: 'journey',
    label: 'Test',
    filename: 'PlatformJourney.cs',
    code: `[ProtoTest]
[SignedInAs]
public async Task
    RestWritesAreVisibleThroughGraphQL()
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
            nodes = new[]
            {
                new
                {
                    name = "atlas",
                    status = ProjectStatuses.Active
                }
            }
        });

    projects.Should.HaveNoErrors();
}`,
    footnote:
      'One journey writes through REST and reads through GraphQL. ProtoTest shares the tenant, sign-in, lifecycle, cleanup and trace.',
  },
  {
    id: 'compose',
    label: 'Setup',
    filename: 'Setup.cs',
    code: `[SetUpFixture]
public sealed class Setup : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder)
    {
        // The application: hosted in-process, reached over three protocols.
        builder.AddApplication(NorthstarTargets.Api, app => app
            .AddAspNetCoreServer<Program>()
            .AddRest(rest => rest.AddClient("Api"))
            .AddGraphQL(graphQL => graphQL.AddClient("GraphQL")));

        // In front of it, behind it, and what the run leaves behind.
        builder.AddWeb();
        builder.AddSql(_ => new SqliteConnection("Data Source=northstar.db"));
        // Tracing stays on its default: each run writes TestResults/prototest-{runId}.prototrace.
        builder.AddSink<HtmlReportSink>();
    }
}`,
    footnote:
      'The short form. Each Add adds a capability. Leave one out and its client, attributes and trace output stay out too. Each package is on the installation page.',
  },
  {
    id: 'evidence',
    label: 'Trace',
    filename: 'prototest summary run.prototrace',
    language: 'text',
    code: `ProtoTest trace 2.0 · run 29e344f9cf54431ca7d8bad3f87a1749
2 tests · 1 failed · 1 succeeded

FAILED orders match their shape (16 ms)
  Shape mismatch failed with 1 error(s):
    • [$.orderId]: Values did not match. (Expected: '7', Actual: '42')
  at artifacts/fixture-gen/Program.cs:65 (Program.<<Main)
  assert.json.shape · failed
  cause: assertion (1 mismatch)
  mismatch: $.orderId: expected 7, actual 42`,
    footnote:
      'Shortened from a sample trace. The summary, the viewer and the agent tools read the same failure from the same archive.',
  },
];

function templateCommands(runner?: string): string[] {
  return ['dotnet new install ProtoTest.Templates', `dotnet new prototest -n Shop${runner ? ` --runner ${runner}` : ''}`];
}

// The template defaults to NUnit, so its row keeps the plain commands a reader copies without thinking.
const runnerChoices: CommandBoxRunner[] = [
  {id: 'nunit', label: 'NUnit (default)', commands: templateCommands()},
  {id: 'xunit', label: 'xUnit v2', commands: templateCommands('xunit')},
  {id: 'xunit3', label: 'xUnit v3', commands: templateCommands('xunit3')},
  {id: 'tunit', label: 'TUnit', commands: templateCommands('tunit')},
  {id: 'mstest', label: 'MSTest', commands: templateCommands('mstest')},
];

function Hero() {
  return (
    <header data-surface="blueprint" className={styles.hero}>
      <div className="container">
        <div className={styles.heroInner}>
          <div>
            <div className={styles.eyebrow}>Composable integration testing for .NET</div>
            <Heading as="h1" className={styles.heroTitle}>
              Test the whole journey. Trace every layer.
            </Heading>
            <p className={styles.heroLead}>
              An integration test checks the app plus its API, database, broker and browser together.
              ProtoTest runs it from one shared setup: REST, GraphQL, SQL, messaging, a browser or a
              spreadsheet share one context, lifecycle, cleanup and trace.
            </p>
            <CommandBox title="Start a project" commands={templateCommands()} runners={runnerChoices} />
            <p className={styles.heroNext}>
              That installs a green suite. <code>dotnet test</code> runs it and writes{' '}
              <code>TestResults/prototest-{'{runId}'}.prototrace</code> plus <code>Shop.html</code>.
            </p>
            <p className={styles.heroLearn}>
              <Link to="/learn/">New to integration testing? Start the learning track</Link>
            </p>
          </div>
          <TabbedCode tabs={heroTabs} label="Three ways a ProtoTest suite looks" />
        </div>
      </div>
    </header>
  );
}

const proofPoints = [
  {
    to: '/docs/project/benchmarks#opencsms-at-1000-tests',
    value: '35-36 ms',
    label: 'per-test median in the 1,000-test product benchmark',
  },
  {
    to: '/docs/project/benchmarks#the-viewer-at-1000-tests',
    value: '1,200 tests',
    label: 'viewer cold open in about 1.3 s',
  },
  {
    to: '/docs/getting-started/installation',
    value: '44 packages',
    label: 'on NuGet, across .NET 8, 9 and 10',
  },
];

/*
 * The numbers a reader comparing frameworks asks for first, each linked to the page that measured it.
 * A quiet row, not a card row: the hero already spent the page's boldness.
 */
function ProofStrip() {
  return (
    <section className={styles.proof} aria-label="ProtoTest measured at scale">
      <div className={`container ${styles.proofInner}`}>
        <span className={styles.proofLead}>Measured</span>
        <div className={styles.proofFacts}>
          {proofPoints.map((point) => (
            <Link key={point.value} className={styles.proofFact} to={point.to}>
              <span className={styles.proofValue}>{point.value}</span>
              <span className={styles.proofLabel}>{point.label}</span>
            </Link>
          ))}
        </div>
      </div>
    </section>
  );
}

function PathsSection() {
  return (
    <section className={`${styles.section} ${styles.sectionAlt}`}>
      <div className="container">
        <div className={styles.sectionHead}>
          <Heading as="h2">Start where you are</Heading>
          <p>Three starting points. Pick the one that fits your situation.</p>
        </div>
        <div className={styles.paths}>
          <div className={styles.pathCard}>
            <Heading as="h3">New to integration testing</Heading>
            <p>
              Start with the Learn track. It begins with why integration tests get hard and works up to
              tests you can trust in CI.
            </p>
            <Link className={styles.pathLink} to="/learn/">
              Start learning
            </Link>
          </div>
          <div className={styles.pathCard}>
            <Heading as="h3">Evaluating ProtoTest</Heading>
            <p>
              Where it wins, where the alternatives win, what a run costs, and the questions teams ask
              before adopting it.
            </p>
            <div className={styles.pathLinks}>
              <Link to="/docs/project/compare">Compare alternatives</Link>
              <Link to="/docs/project/benchmarks">Cost at scale</Link>
              <Link to="/docs/project/faq">Adoption questions</Link>
            </div>
          </div>
          <div className={styles.pathCard}>
            <Heading as="h3">Already have a suite</Heading>
            <p>
              Recipes for common journeys, the conversion order for an xUnit suite you already have, and the
              pages to reach for when a run needs explaining.
            </p>
            <div className={styles.pathLinks}>
              <Link to="/docs/recipes/overview">Recipes</Link>
              <Link to="/docs/runners/bring-your-existing-suite">Bring an existing xUnit suite</Link>
              <Link to="/docs/getting-started/migrating-from-1-0">Migrate from 1.0</Link>
              <Link to="/docs/getting-started/troubleshooting">Troubleshooting</Link>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}

function FailureSection() {
  return (
    <section className={styles.section}>
      <div className="container">
        <div className={styles.sectionHead}>
          <Heading as="h2">Learn by failure</Heading>
          <p>
            A passing suite tells you little. A failure shows what changed, what was not ready, or
            what cleanup hid the cause. The lessons start from those failures. The demo trace records
            every layer of one failed test.
          </p>
        </div>
        <FailureGallery />
      </div>
    </section>
  );
}

function TraceSection() {
  return (
    <section className={`${styles.section} ${styles.sectionAlt}`}>
      <div className="container">
        <div className={styles.featureRow}>
          <div className={styles.featureCopy}>
            <Heading as="h2">Following a failed test</Heading>
            <p>
              Step through the three views of the demo trace: the run, the failed test story, and
              the check. You see the changed values, the exception, and the asserting line.
            </p>
            <div className={styles.featureLinks}>
              <Link className={styles.featureLink} href="https://trace.prototest.dev/?demo=1">
                Open the failing trace →
              </Link>
              <Link className={styles.featureLink} to="/docs/observability/prototrace">
                How ProtoTrace works →
              </Link>
            </div>
          </div>
          <ViewerWalkthrough />
        </div>
      </div>
    </section>
  );
}

function AgentExchange() {
  return (
    <Frame
      head={
        <>
          <strong>get_failure</strong>
          <span className={styles.frameMeta}>run 29e344f9 · fixture example</span>
        </>
      }
      foot={
        <>
          Trimmed from the committed MCP fixture trace. The tool is read-only and capped; the whole
          document is on <Link to="/docs/agent-workflows/diagnosis">Diagnosis</Link>.
        </>
      }>
      <CodeSnippet
        language="json"
        code={`{
  "runId": "29e344f9cf54431ca7d8bad3f87a1749",
  "test": {
    "testId": "00002",
    "name": "orders match their shape",
    "outcome": "failed",
    "durationMs": 16
  },
  "failure": {
    "kind": "assert.json.shape",
    "status": "failed",
    "errorType": "ProtoTest.Json.JsonShapeMismatchException",
    "errorMessage": "Shape mismatch failed with 1 error(s): [$.orderId]: Values did not match. (Expected: '7', Actual: '42')",
    "sourceFile": "artifacts/fixture-gen/Program.cs",
    "sourceLine": 65
  }
}`}
      />
    </Frame>
  );
}

function AgentSection() {
  return (
    <section className={styles.section}>
      <div className="container">
        <div className={styles.featureRow}>
          <div className={styles.featureCopy}>
            <Heading as="h2">Point your agent at the trace</Heading>
            <p>
              <code>ProtoTest.Mcp</code> is a local server that reads the <code>.prototrace</code>{' '}
              archives in a repository. An agent works the evidence loop through four read-only tools:{' '}
              <code>list_runs</code>, <code>get_failure</code>, <code>get_diagnosis</code> and{' '}
              <code>get_coverage</code>.
            </p>
            <p>
              Every answer comes from the recorded trace, with size limits. The server opens no port
              and uploads nothing. It returns the same failure the viewer shows.
            </p>
            <Link className={styles.featureLink} to="/docs/agent-workflows/setup">
              Connect an agent →
            </Link>
          </div>
          <AgentExchange />
        </div>
      </div>
    </section>
  );
}

function CtaSection() {
  return (
    <section className={styles.section}>
      <div className="container">
        <div className={styles.ctaBanner}>
          <Heading as="h2">Try the starter project</Heading>
          <p>
            The template creates a small ASP.NET Core API and a ProtoTest suite. Run the commands
            from the <Link to="/docs/getting-started/installation">installation page</Link>, then
            run the suite. A green run ends like this (abridged), with the trace and the report
            beside it:
          </p>
          <div className={styles.ctaSnippet}>
            <CodeSnippet
              language="text"
              code={`dotnet test
Passed! - Failed: 0, Passed: 1 - Shop.Tests.dll
TestResults/prototest-{runId}.prototrace
TestResults/Shop.html`}
            />
          </div>
          <p className={styles.ctaRelease}>
            1.1.0 adds readiness probes and a per-test clock.{' '}
            <Link to="/changelog">Full list in the changelog →</Link>
          </p>
          <div className={styles.heroButtons}>
            <Link className={`${styles.btn} ${styles.btnPrimary}`} to="/docs/getting-started/first-test">
              Your first test
            </Link>
          </div>
        </div>
      </div>
    </section>
  );
}

export default function Home(): ReactNode {
  return (
    <Layout
      title="Test the whole journey. Trace every layer."
      description="ProtoTest is a foundation for composing .NET integration tests around one context, lifecycle and trace.">
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
              'A foundation for composing .NET integration tests around one context, lifecycle and trace.',
          })}
        </script>
      </Head>
      <Hero />
      <main>
        <ProofStrip />
        <FailureSection />
        <TraceSection />
        <AgentSection />
        <PathsSection />
        <CtaSection />
      </main>
    </Layout>
  );
}
