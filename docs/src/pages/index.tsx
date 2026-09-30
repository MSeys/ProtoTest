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

const proofPoints = [
  {
    to: '/docs/project/benchmarks#opencsms-at-1000-tests',
    value: '35-36 ms',
    label: 'per-test median, 1,000-test benchmark',
  },
  {
    to: '/docs/project/benchmarks#the-viewer-at-1000-tests',
    value: '1.3 s',
    label: 'viewer cold open, 1,200 tests',
  },
  {
    to: '/docs/getting-started/installation',
    value: '44 packages',
    label: 'on NuGet, .NET 8, 9 and 10',
  },
];

/*
 * First screen: the failure-first claim, the command that starts a suite, and the
 * measured cost beside it. One centered column so the action reads first on every width.
 */
function Hero() {
  return (
    <header data-surface="blueprint" className={styles.hero}>
      <div className="container">
        <div className={styles.heroInner}>
          <div className={styles.eyebrow}>Composable integration testing for .NET</div>
          <Heading as="h1" className={styles.heroTitle}>
            Integration tests that explain their own failures.
          </Heading>
          <p className={styles.heroLead}>
            A journey across your API, database, broker and browser usually fails with nothing to
            go on. ProtoTest runs it from one shared setup, and every run writes a trace that
            shows what changed, what was not ready, and which line asserted it.
          </p>
          <div className={styles.heroAction}>
            <CommandBox title="Start a project" commands={templateCommands()} runners={runnerChoices} />
            <p className={styles.heroNext}>
              That installs a green suite. <code>dotnet test</code> runs it and writes{' '}
              <code>TestResults/prototest-{'{runId}'}.prototrace</code> plus <code>Shop.html</code>.
            </p>
          </div>
          <div className={styles.heroButtons}>
            <Link className={`${styles.btn} ${styles.btnPrimary}`} to="/docs/getting-started/first-test">
              Your first test
            </Link>
            <Link className={`${styles.btn} ${styles.btnGhost}`} href="https://trace.prototest.dev/?demo=1">
              Open a failing trace
            </Link>
          </div>
          <ul className={styles.heroProof} aria-label="ProtoTest measured at scale">
            {proofPoints.map((point) => (
              <li key={point.value}>
                <Link to={point.to}>
                  <strong>{point.value}</strong> {point.label}
                </Link>
              </li>
            ))}
          </ul>
          <p className={styles.heroLearn}>
            <Link to="/learn/">New to integration testing? Start the learning track</Link>
          </p>
        </div>
      </div>
    </header>
  );
}

function FailureSection() {
  return (
    <section className={styles.section}>
      <div className="container">
        <div className={styles.sectionHead}>
          <Heading as="h2">When a test fails, the trace answers</Heading>
          <p>
            A passing suite tells you little. A failure shows what changed, what was not ready, or
            what cleanup hid the cause. Each card below is one recorded failure and its fix.
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
        <div className={styles.sectionHead}>
          <Heading as="h2">Follow one failure end to end</Heading>
          <p>
            Step through the three views of the demo trace: the run, the failed test story, and
            the check. You see the changed values, the exception, and the asserting line.
          </p>
        </div>
        <ViewerWalkthrough />
        <div className={styles.agentStrip}>
          <div className={styles.agentCopy}>
            <Heading as="h3">Agents read the same trace</Heading>
            <p>
              <code>ProtoTest.Mcp</code> is a local server over the <code>.prototrace</code>{' '}
              archives in a repository, through four read-only tools. It opens no port and uploads
              nothing. <Link to="/docs/agent-workflows/setup">Connect an agent →</Link>
            </p>
          </div>
          <Frame
            head={
              <>
                <strong>get_failure</strong>
                <span className={styles.frameMeta}>run 29e344f9 · fixture example</span>
              </>
            }
            foot={
              <>
                Trimmed from the committed MCP fixture trace. The whole document is on{' '}
                <Link to="/docs/agent-workflows/diagnosis">Diagnosis</Link>.
              </>
            }>
            <CodeSnippet
              language="json"
              code={`{
  "test": "orders match their shape",
  "outcome": "failed",
  "kind": "assert.json.shape",
  "error": "[\\$.orderId]: expected 7, actual 42"
}`}
            />
          </Frame>
        </div>
      </div>
    </section>
  );
}

function ComposeSection() {
  return (
    <section className={styles.section}>
      <div className="container">
        <div className={styles.sectionHead}>
          <Heading as="h2">One context for the whole journey</Heading>
          <p>
            REST, GraphQL, SQL, messaging and the browser share one context, lifecycle, cleanup
            and trace. The test reads like the scenario; the setup lists what the suite touches.
          </p>
        </div>
        <div className={styles.composeInner}>
          <TabbedCode tabs={heroTabs} label="A ProtoTest suite: the test, the setup, the trace" />
        </div>
        <p className={styles.composeLinks}>
          <Link to="/docs/getting-started/installation">Installation →</Link>
          <Link to="/docs/project/compare">How this compares →</Link>
          <Link to="/docs/project/benchmarks">Cost at scale →</Link>
        </p>
      </div>
    </section>
  );
}

function PathsRow() {
  return (
    <section className={`${styles.section} ${styles.pathsRow}`}>
      <div className="container">
        <nav className={styles.pathsNav} aria-label="Start where you are">
          <span className={styles.pathsLabel}>Start where you are:</span>
          <Link to="/learn/">Learn track</Link>
          <Link to="/docs/project/compare">Comparison</Link>
          <Link to="/docs/recipes/overview">Recipes</Link>
          <Link to="/docs/getting-started/troubleshooting">Troubleshooting</Link>
          <Link to="/docs/project/faq">Adoption questions</Link>
        </nav>
      </div>
    </section>
  );
}

function CtaStrip() {
  return (
    <section className={styles.cta}>
      <div className="container">
        <p className={styles.ctaLine}>
          <strong>Green in minutes:</strong> run the two commands above, then{' '}
          <code>dotnet test</code>. <Link to="/docs/getting-started/first-test">Your first test →</Link>
        </p>
      </div>
    </section>
  );
}

export default function Home(): ReactNode {
  return (
    <Layout
      title="Integration tests that explain their own failures."
      description="ProtoTest runs .NET integration tests from one shared setup, and every run writes a trace that explains each failure.">
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
              'ProtoTest runs .NET integration tests from one shared setup, and every run writes a trace that explains each failure.',
          })}
        </script>
      </Head>
      <Hero />
      <main>
        <FailureSection />
        <TraceSection />
        <ComposeSection />
        <PathsRow />
        <CtaStrip />
      </main>
    </Layout>
  );
}
