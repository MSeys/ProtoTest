import type {ReactNode} from 'react';
import Link from '@docusaurus/Link';
import Head from '@docusaurus/Head';
import Layout from '@theme/Layout';
import Heading from '@theme/Heading';

import CommandBox from '@site/src/components/CommandBox';
import CodeSnippet from '@site/src/components/CodeSnippet';
import Frame from '@site/src/components/Frame';
import TabbedCode, {type CodeTab} from '@site/src/components/TabbedCode';
import ReleaseFeed from '@site/src/components/ReleaseFeed';
import ViewerWalkthrough from '@site/src/components/ViewerWalkthrough';
import FailureGallery from '@site/src/components/FailureGallery';
import styles from './index.module.css';

const heroTabs: CodeTab[] = [
  {
    id: 'journey',
    label: 'One journey',
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
      'One journey writes through REST and reads through GraphQL. Tenant, sign-in, lifecycle, cleanup and trace are shared automatically.',
  },
  {
    id: 'compose',
    label: 'Compose',
    filename: 'Setup.cs',
    code: `[SetUpFixture]
public sealed class Setup : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder)
    {
        // The application: hosted in-process, reached over three protocols.
        builder.AddApplication(NorthstarTargets.Api, app => app
            .AddAspNetCoreServer<Program>()
            .AddRest(rest => rest.AddClient("Api")
                .AddCollector<OpenApiCoverageCollector>())
            .AddGraphQL(graphQL => graphQL.AddClient("GraphQL")
                .WithSchemaCoverage("northstar.graphql"))
            .AddGrpc(grpc => grpc.AddClient("Api")));

        // In front of it, behind it, and what a scenario needs.
        builder.AddWeb();
        builder.AddSql(_ => new SqliteConnection("Data Source=northstar.db"));
        builder.AddEntityFrameworkCore<BillingDbContext>((services, options) =>
            options.UseSqlite(services.GetRequiredService<DbConnection>()));
        builder.AddMessaging(messaging => messaging.UseRabbitMq());
        builder.AddSheets();
        builder.AddData(data => data.AddDefaults<NorthstarDataDefaults>());

        // What the run leaves behind.
        builder.ConfigureTracing(trace => trace.OutputPath = "northstar.prototrace");
        builder.AddSink<HtmlReportSink>();
    }
}`,
    footnote:
      'Each Add… is a capability. Compose the ones your suite needs; leave one out and its client, its attributes and its part of the trace simply are not there.',
  },
  {
    id: 'evidence',
    label: 'Evidence',
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
      'Abridged from prototest summary on the committed MCP fixture trace. The summary, the viewer and the MCP tools select the same failure from the same archive.',
  },
  {
    id: 'agent',
    label: 'Agent',
    filename: '.mcp.json',
    language: 'json',
    code: `{
  "mcpServers": {
    "prototest": {
      "command": "prototest-mcp",
      "args": ["--project", "."]
    }
  }
}`,
    footnote:
      'One local stdio server for the runs in your repository. It answers four read-only tools; the section below shows what get_failure returns.',
  },
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
              ProtoTest brings the setup around an integration test into one place. Compose REST, GraphQL,
              SQL, messaging, a browser or a spreadsheet; they share one context, lifecycle, cleanup and
              trace.
            </p>
            <CommandBox
              title="Start a project"
              commands={['dotnet new install ProtoTest.Templates', 'dotnet new prototest -n Shop']}
            />
            <div className={styles.heroLinks}>
              <Link
                className={`${styles.btn} ${styles.btnSecondary}`}
                href="https://trace.prototest.dev/?demo=1">
                See a failing trace
              </Link>
              <Link className={styles.heroLearn} to="/learn/">
                New to integration testing? Start the learning track
              </Link>
            </div>
          </div>
          <TabbedCode tabs={heroTabs} label="Four ways a ProtoTest suite looks" />
        </div>
      </div>
    </header>
  );
}

function PathsSection() {
  return (
    <section className={`${styles.section} ${styles.sectionAlt}`}>
      <div className="container">
        <div className={styles.sectionHead}>
          <Heading as="h2">Start where you are</Heading>
          <p>Three ways in. Pick the one that matches what you bring.</p>
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
              <Link to="/docs/project/compare">Comparison</Link>
              <Link to="/docs/project/benchmarks">Benchmarks</Link>
              <Link to="/docs/project/faq">FAQ</Link>
            </div>
          </div>
          <div className={styles.pathCard}>
            <Heading as="h3">Already have a suite</Heading>
            <p>
              Recipes for common journeys, and the pages to reach for when a run needs explaining.
            </p>
            <div className={styles.pathLinks}>
              <Link to="/docs/recipes/overview">Recipes</Link>
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
            A green suite teaches you little. The useful part is what happens when a test fails: a value
            that changed, a service that was not ready, a cleanup that hid the cause. The lessons start
            from failures like those, and the demo trace is a failed test with every layer recorded.
          </p>
          <div className={styles.featureLinks}>
            <Link className={styles.featureLink} href="https://trace.prototest.dev/?demo=1">
              Open the failing trace
            </Link>
            <Link className={styles.featureLink} to="/learn/">
              Start the track
            </Link>
          </div>
        </div>
        <FailureGallery />
      </div>
    </section>
  );
}

function TraceSection() {
  return (
    <section className={styles.section}>
      <div className="container">
        <div className={styles.featureRow}>
          <div className={styles.featureCopy}>
            <Heading as="h2">Following a failed test</Heading>
            <p>
              Step through the three views of the demo trace: the run first, then the failed test's story,
              then the check itself. The values that differed, the exception, and the line that made the
              assertion.
            </p>
            <div className={styles.featureLinks}>
              <Link className={styles.featureLink} href="https://trace.prototest.dev/?demo=1">
                Open the failing trace ↗
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
          <span className={styles.frameMeta}>run 29e344f9 · fixture trace</span>
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
    "durationMs": 16.4395
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
    <section className={`${styles.section} ${styles.sectionAlt}`}>
      <div className="container">
        <div className={styles.featureRow}>
          <div className={styles.featureCopy}>
            <Heading as="h2">Point your agent at the trace</Heading>
            <p>
              <code>ProtoTest.Mcp</code> is a local stdio server that reads the <code>.prototrace</code>{' '}
              archives in a repository. An agent works the evidence loop through four read-only tools:{' '}
              <code>list_runs</code>, <code>get_failure</code>, <code>get_diagnosis</code> and{' '}
              <code>get_coverage</code>.
            </p>
            <p>
              Every answer is the recorded evidence, capped and deterministic. The server binds no port and
              uploads nothing, and the failure it returns is the one the viewer shows.
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
            The template creates a small ASP.NET Core API and a ProtoTest suite. Run it locally and open
            the trace it writes.
          </p>
          <div className={styles.heroButtons}>
            <Link className={`${styles.btn} ${styles.btnPrimary}`} to="/docs/getting-started/first-test">
              Write your first test
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
        <PathsSection />
        <FailureSection />
        <TraceSection />
        <AgentSection />
        <CtaSection />
      </main>
      <ReleaseFeed />
    </Layout>
  );
}
