import type {ReactNode} from 'react';
import Link from '@docusaurus/Link';
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
            <div className={styles.eyebrow}>Integration testing for .NET</div>
            <Heading as="h1" className={styles.heroTitle}>
              A composable integration-testing foundation.
            </Heading>
            <p className={styles.heroLead}>
              Pick the capabilities your tests need — REST, GraphQL, the browser, test data, SQL, an
              in-process ASP.NET Core host — and compose them onto one execution context. ProtoTest coordinates
              their lifecycle and traces everything they do, so a failing test shows the check that failed,
              the values it compared and every step before it.
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

/*
 * The positioning: most teams with a serious suite end up building this themselves, privately and for one
 * application. Each card pairs what that in-house foundation tends to become with what ProtoTest does instead.
 */
const inHouse = [
  {
    before: 'Built for one application',
    after: 'Generic by design',
    text: 'Capabilities are composed per suite. Your domain — tenants, sign-in, test data — lives in attributes and builders you write on top, not inside the framework.',
  },
  {
    before: 'Understood by one person',
    after: 'Documented in the open',
    text: 'Every capability has a page, the recipes show them combined, and a failing test explains itself through its trace to whoever opens it.',
  },
  {
    before: 'Rebuilt for the next project',
    after: 'The same packages everywhere',
    text: 'A second service, a second team, a second repository: the same NuGet packages, the same patterns and the same trace format, from the first test.',
  },
];

function InHouseSection() {
  return (
    <section className={`${styles.section} ${styles.sectionAlt}`}>
      <div className="container">
        <div className={styles.sectionHead}>
          <Heading as="h2">The framework your team was going to build anyway.</Heading>
          <p>
            Every .NET team with a serious integration suite ends up with one: a fixture that boots the
            application, helpers that sign users in, builders for test data, cleanup that mostly works, and
            logging for the day CI fails. It usually stays internal. ProtoTest is that foundation, public and
            generic, so you start from it instead of growing your own.
          </p>
        </div>
        <div className={styles.inHouse}>
          {inHouse.map((item) => (
            <div key={item.after} className={styles.inHouseCard}>
              <span className={styles.inHouseBefore}>
                <s>{item.before}</s>
              </span>
              <Heading as="h3">{item.after}</Heading>
              <p>{item.text}</p>
            </div>
          ))}
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
            <Heading as="h2">When a test fails, the trace explains it.</Heading>
            <p>
              Everything you compose is traced: each hook, client, request and check, per phase, with what it
              changed. Nothing to log, nothing to instrument.
            </p>
            <p>
              This is the REST test from above, run with a deliberately wrong expectation. The trace leads with
              the check that failed, the line of code that made it and the values it compared, then shows every step around it — the tenant
              the attribute created, the call, the cleanup.
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
          <Heading as="h2">Most of an integration test isn't the test.</Heading>
          <p>
            The same scenario, the same assertions, the same in-process application. Hatched is the
            plumbing a fixture carries; solid is the scenario itself. Open any task to see the code from both sides.
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
          <Heading as="h2">Beyond pass or fail.</Heading>
          <p>
            Because ProtoTest coordinates the lifecycle and understands its own integrations, it can
            report on the run without you instrumenting anything.
          </p>
        </div>

        <div className={styles.featureRow}>
          <div className={styles.featureCopy}>
            <Heading as="h3">Your assertions become a coverage map.</Heading>
            <p>
              Point a collector at your OpenAPI document or GraphQL schema and ProtoTest walks the
              whole contract — every endpoint, every declared response, every response property —
              and reports which ones your suite actually asserted.
            </p>
            <p>
              Not "how many lines executed". Which parts of your API's surface no test has ever
              looked at.
            </p>
            <Link className={styles.featureLink} to="/docs/observability/coverage">
              How coverage works →
            </Link>
          </div>
          <CoverageMap />
        </div>

        <div className={`${styles.featureRow} ${styles.featureRowReverse}`}>
          <div className={styles.featureCopy}>
            <Heading as="h3">The whole run travels as one file.</Heading>
            <p>
              Every hook, client, request, check and artifact lands in one portable <code>.prototrace</code>{' '}
              file, with what existed and changed while each test ran. Open it in the viewer on your machine or
              straight from a CI artifact; nothing is uploaded.
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
          <Heading as="h2">One foundation. Every layer.</Heading>
          <p>
            A test can stand anywhere: drive the browser at one end, read the store the application writes at
            the other. Every capability plugs into the same <code>ProtoExecutionContext</code>, so the depth you
            choose changes what a test sees, not how it is written, and every depth lands in the same trace.
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
          <Heading as="h2">Start with one test.</Heading>
          <p>
            ProtoTest is under active development, built in the open. The foundation is real — and
            the sample app in the repository runs every layer shown here.
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
      title="ProtoTest — a composable integration-testing foundation for .NET"
      description="ProtoTest is a composable integration-testing foundation for .NET: compose REST, GraphQL, browser, data and SQL capabilities onto one execution context, and get a trace of everything they do.">
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
