import type {ReactNode} from 'react';
import Link from '@docusaurus/Link';
import Head from '@docusaurus/Head';
import Layout from '@theme/Layout';
import Heading from '@theme/Heading';

import TabbedCode, {type CodeTab} from '@site/src/components/TabbedCode';
import Comparison from '@site/src/components/Comparison';
import {comparisonConcerns, withoutProtoTest, withProtoTest} from '@site/src/data/comparison';
import CoverageMap from '@site/src/components/CoverageMap';
import TraceView from '@site/src/components/TraceView';
import CapabilityIndex from '@site/src/components/CapabilityIndex';
import VisibilityPanel from '@site/src/components/VisibilityPanel';
import styles from './index.module.css';

const heroTabs: CodeTab[] = [
  {
    id: 'journey',
    label: 'Journey',
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

    projects.ShouldHaveNoErrors();
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
    id: 'rest',
    label: 'REST',
    filename: 'DiagnosticsShowcase.cs',
    code: `[Application(NorthstarTargets.Api)]
[NorthstarTenant]
[Auth<NorthstarAuthenticator>]
public sealed class DiagnosticsShowcase
{
    [ProtoTest]
    [SignedInAs]
    public async Task TheOrganizationReportsItsPlanAndProjectCount()
    {
        using var organization = await Proto.Context.Rest()
            .GetAsync("/api/v1/organization");

        organization
            .Should.HaveHttpStatus(HttpStatusCode.OK)
            .ShouldMatchShape(new
            {
                projectCount = 99,
                planId = "nonexistent-plan"
            });
    }
}`,
    footnote:
      'The tenant, the sign-in and the cleanup are attributes; the shape is the assertion. This is the test whose trace is shown below.',
  },
  {
    id: 'graphql',
    label: 'GraphQL',
    filename: 'GraphQLControlPlaneTests.cs',
    code: `var expected = new
{
    id = JsonValue.GreaterThan(0),
    product = "live-notebook",
    total = 15m,
    status = "pending"
};

await using var subscription = await Proto.Context
    .GraphQL()
    .Subscription("orderCreated")
    .Select(expected)
    .SubscribeAsync();

// ... mutation fires an orderCreated event ...

using var next = await subscription
    .ExpectNextAsync(expected, timeout.Token);`,
    footnote:
      'One anonymous object does both jobs: it builds the selection set and asserts the payload.',
  },
  {
    id: 'grpc',
    label: 'gRPC',
    filename: 'OrderTests.cs',
    code: `[ProtoTest]
public async Task A_placed_order_can_be_read_back()
{
    var order = await Proto.Context.Grpc().UnaryAsync(
        Orders.GetOrder,
        new GetOrderRequest { Id = 42 });

    order.ShouldMatchShape(new
    {
        id = 42,
        status = "PENDING",
        total = JsonValue.GreaterThan(0),
        lines = new[]
        {
            new { sku = "notebook", quantity = 2 }
        }
    });
}`,
    footnote:
      'The protobuf reply is matched with the same shapes as REST and GraphQL: declare only the fields the behaviour depends on, nested and partial.',
  },
  {
    id: 'browser',
    label: 'Browser',
    filename: 'PortalSignInTests.cs',
    code: `[ProtoTest]
[LoginAs<BackOfficeLogin>("billing.admin")]
public async Task Sign_in_shows_the_dashboard()
{
    var login = Proto.Context.Web().Page<LoginPage>();
    await login.OpenAsync("https://portal.example.test");

    await login.Form.Flow("Sign in")
        .Fill(form => form.Password, "correct horse")
        .Check(form => form.RememberMe)
        .Click(form => form.Submit)
        .RunAsync();

    await login.Form.Status.ShouldHaveTextAsync(
        "Signed in", TimeSpan.FromSeconds(2));
}`,
    footnote:
      'A named flow records as one web.flow operation with each step nested under it — and the same page objects run on Playwright or Selenium.',
  },
  {
    id: 'data',
    label: 'Data',
    filename: 'CommerceAndSecurityTests.cs',
    code: `var members = await Proto.Context.Data()
    .For<CreateUserRequest>()
    .With(request => request.Role, SampleRoles.Member)
    .CreateManyAsync<UserResponse>(7);

var admin = Proto.Context.Resolve<SampleUserContext>();

using var response = await Proto.Context.Rest()
    .GetAsync("/api/admin/users");

response.ShouldMatchShape(new
{
    tenant = admin.Tenant,
    memberCount = members.Count + 1   // the seven, and the admin
});`,
    footnote:
      'Defaults come from your data module, provisioning from your provisioner — and it shares the test context with every other client.',
  },
  {
    id: 'sql',
    label: 'SQL',
    filename: 'InvoiceTests.cs',
    code: `[ProtoTest]
public async Task Paying_an_invoice_marks_it_paid()
{
    using var response = await Proto.Context.Rest()
        .PostAsync("/api/invoices/42/pay");
    response.Should.HaveHttpStatus(HttpStatusCode.OK);

    // The store the application writes to, on the
    // connection and transaction the test owns.
    var billing = Proto.Context.Sql<BillingDbContext>();
    var invoice = await billing.Invoices.SingleAsync(
        invoice => invoice.Id == 42);

    Assert.That(invoice.Status, Is.EqualTo("paid"));
}`,
    footnote:
      'ProtoTest opens the connection, begins the transaction and rolls it back, so a test can read and write the real store without leaving anything behind.',
  },
  {
    id: 'messaging',
    label: 'Messaging',
    filename: 'BillingEventTests.cs',
    code: `[ProtoTest]
public async Task Paying_an_invoice_publishes_invoice_paid()
{
    using var response = await Proto.Context.Rest()
        .PostAsync("/api/invoices/42/pay");
    response.Should.HaveHttpStatus(HttpStatusCode.OK);

    await Proto.Context.Messaging().AwaitAsync(
        "invoice.paid",
        message => message.Payload!.Contains("\\"id\\":42"),
        TimeSpan.FromSeconds(15));
}`,
    footnote:
      'Await the event the call should cause. The same test runs on the in-memory broker or on RabbitMQ; a timeout fails it with what did arrive.',
  },
  {
    id: 'sheets',
    label: 'Sheets',
    filename: 'SalesReportTests.cs',
    code: `[Sheet("Sales", HeaderRows = [1, 2])]
public sealed record SalesRow(
    [property: Column("Region", Pattern = "^[A-Z]+$", Unique = true)] string Region,
    [property: Column("FY26", "Amount", Min = 0)] decimal Amount,
    [property: Column("FY26", "Count", Min = 0)] int Count);

[ProtoTest]
public async Task The_sales_report_ranks_regions_by_amount()
{
    using var response = await Proto.Context.Rest()
        .GetAsync("/api/v1/reports/sales.xlsx");

    var sales = Proto.Context.Sheets().Open(response).Model<SalesRow>();
    sales.Verify();

    sales.Column(row => row.Amount).ShouldBeSortedBy(ascending: false);
    sales.Row(row => row.Region == "EMEA")
        .ShouldMatchShape(new { Amount = 1200m, Count = 12 });
}`,
    footnote:
      'The record is the sheet: header paths bind the columns, and Verify checks every row against their rules and reports every violation at once.',
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
              ProtoTest brings the setup around an integration test into one place. Choose the integrations a
              suite needs; they share the same context, lifecycle, cleanup and trace. A test can call an API,
              wait for an event, inspect a database, drive a browser or verify a generated file.
            </p>
            <div className={styles.heroButtons}>
              <Link className={`${styles.btn} ${styles.btnPrimary}`} to="/docs/getting-started/installation">
                Get started
              </Link>
              <Link className={`${styles.btn} ${styles.btnSecondary}`} to="https://github.com/MSeys/ProtoTest">
                View on GitHub
              </Link>
            </div>
          </div>
          <TabbedCode tabs={heroTabs} />
        </div>
      </div>
    </header>
  );
}

function InHouseSection() {
  return (
    <section className={`${styles.section} ${styles.sectionAlt}`}>
      <div className="container">
        <div className={`${styles.sectionHead} ${styles.originCopy}`}>
          <Heading as="h2">Why I built ProtoTest</Heading>
          <p>
            ProtoTest comes from things I have struggled with while writing integration tests. Once a suite grows,
            a lot of the work shifts to setup, infrastructure and figuring out failures that only happen sometimes.
            I wanted the different integrations to work from the same context and lifecycle instead of each one
            solving that again.
          </p>
          <p>
            Application-specific setup still belongs to the test project. ProtoTest provides the host, context,
            lifecycle, tracing and integration points underneath it.
          </p>
        </div>
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
              This example uses a deliberately wrong REST expectation. The trace starts at the failed check,
              shows the values that differed and keeps the setup, request and cleanup around it. Hooks, clients,
              requests and checks are recorded under the phase where they ran.
            </p>
            <div className={styles.featureLinks}>
              <Link className={styles.featureLink} href="https://trace.prototest.dev/?demo=1">
                Open a sample trace ↗
              </Link>
              <Link className={styles.featureLink} to="/docs/observability/prototrace">
                How ProtoTrace works →
              </Link>
            </div>
          </div>
          <TraceView />
        </div>
      </div>
    </section>
  );
}

function ComparisonSection() {
  return (
    <section className={`${styles.section} ${styles.sectionAlt}`}>
      <div className="container">
        <div className={styles.sectionHead}>
          <Heading as="h2">What stays in the test</Heading>
          <p>
            This is the same scenario against the same in-process application, first with a regular fixture and
            then with ProtoTest. Hatched lines are setup; solid lines belong to the scenario. Open a task to see
            the code from both sides.
          </p>
        </div>
        <Comparison without={withoutProtoTest} with={withProtoTest} concerns={comparisonConcerns} />
      </div>
    </section>
  );
}

function PayoffSection() {
  return (
    <section className={styles.section}>
      <div className="container">
        <div className={styles.sectionHead}>
          <Heading as="h2">Coverage and run output</Heading>
          <p>
            Integrations record observations while a test runs. Collectors use those observations to build
            coverage and reports for the complete run.
          </p>
        </div>

        <div className={styles.featureRow}>
          <div className={styles.featureCopy}>
            <Heading as="h3">Contract coverage</Heading>
            <p>
              OpenAPI and GraphQL collectors compare recorded calls and assertions with the contract. The report
              shows which endpoints, responses, properties and fields the suite reached or checked.
            </p>
            <p>
              This is contract coverage, not source-code coverage.
            </p>
            <Link className={styles.featureLink} to="/docs/observability/coverage">
              How coverage works →
            </Link>
          </div>
          <CoverageMap />
        </div>

        <div className={`${styles.featureRow} ${styles.featureRowReverse}`}>
          <div className={styles.featureCopy}>
            <Heading as="h3">Portable trace files</Heading>
            <p>
              A <code>.prototrace</code> file contains the hooks, clients, requests, checks and artifacts recorded
              during the run. It can be opened locally or downloaded from CI and opened in the viewer; the viewer
              does not upload it.
            </p>
            <p>
              The trace also says what it could not see: whether the application ran in-process, which
              capabilities were composed, and whether the application reported its own values.
            </p>
            <Link className={styles.featureLink} to="/docs/observability/prototrace">
              What a trace file holds →
            </Link>
          </div>
          <VisibilityPanel />
        </div>
      </div>
    </section>
  );
}

function LayersSection() {
  return (
    <section className={`${styles.section} ${styles.sectionAlt}`}>
      <div className="container">
        <div className={styles.sectionHead}>
          <Heading as="h2">Integrations share one context</Heading>
          <p>
            Browser, API, database, messaging, data and document integrations plug into the same{' '}
            <code>ProtoExecutionContext</code>. A test can use only one of them or combine several, while their
            operations still land in the same trace.
          </p>
          <div className={styles.featureLinks}>
            <Link className={styles.featureLink} to="/docs/integrations/overview">
              Every integration →
            </Link>
            <Link className={styles.featureLink} to="/docs/recipes/overview">
              Combined in one test →
            </Link>
          </div>
        </div>
        <CapabilityIndex />
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
            The template creates a small ASP.NET Core API and a ProtoTest suite that you can run locally.
          </p>
          <div className={`${styles.heroButtons} ${styles.heroButtonsCenter}`}>
            <Link className={`${styles.btn} ${styles.btnPrimary}`} to="/docs/getting-started/first-test">
              Write your first test
            </Link>
            <Link className={`${styles.btn} ${styles.btnSecondary}`} to="https://github.com/MSeys/ProtoTest">
              Star on GitHub
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
            softwareVersion: '1.0',
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
        <InHouseSection />
        <TraceSection />
        <ComparisonSection />
        <PayoffSection />
        <LayersSection />
        <CtaSection />
      </main>
    </Layout>
  );
}
