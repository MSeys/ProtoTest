import type {ReactNode} from 'react';
import Link from '@docusaurus/Link';
import Layout from '@theme/Layout';
import Heading from '@theme/Heading';

import TabbedCode, {type CodeTab} from '@site/src/components/TabbedCode';
import Comparison from '@site/src/components/Comparison';
import {comparisonConcerns, withoutProtoTest, withProtoTest} from '@site/src/data/comparison';
import CoverageMap from '@site/src/components/CoverageMap';
import TraceView from '@site/src/components/TraceView';
import LayerStack from '@site/src/components/LayerStack';
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
        builder.AddApplication(NorthstarTargets.Api, app => app
            .AddAspNetCoreServer<Program>()
            .AddRest(rest => rest.AddClient("Api")
                .AddCollector<OpenApiCoverageCollector>())
            .AddGraphQL(graphQL => graphQL.AddClient("GraphQL")
                .WithSchemaCoverage("northstar.graphql")));

        builder.AddData(data =>
            data.AddDefaults<NorthstarDataDefaults>());
        builder.AddSql(_ =>
            new SqliteConnection("Data Source=northstar.db"));

        builder.ConfigureTracing(trace =>
            trace.OutputPath = "northstar.prototrace");
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
            .ShouldHaveHttpStatus(HttpStatusCode.OK)
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
    users = JsonValue.NotNull()
});`,
    footnote:
      'Defaults come from your data module, provisioning from your provisioner — and it shares the test context with every other client.',
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
              in-process ASP.NET Core host — and compose them onto one execution context. ProtoTest owns the
              lifecycle around them and traces everything they do, so a failing test shows the check that
              failed, the values it compared and every step before it.
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
              the check that failed and the values it compared, then shows every step around it — the tenant
              the attribute created, the call, the cleanup.
            </p>
            <Link className={styles.featureLink} to="/docs/advanced/prototrace">
              How ProtoTrace works →
            </Link>
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
            Because ProtoTest owns the lifecycle and understands its own integrations, it can report
            on the run without you instrumenting anything.
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
            <Link className={styles.featureLink} to="/docs/advanced/coverage">
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
            <Link className={styles.featureLink} to="/docs/advanced/prototrace">
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
            Each layer is a client on the same <code>ProtoExecutionContext</code>, so what you learn
            on one carries to the next — and new layers slot in without changing how your tests are
            written.
          </p>
        </div>
        <LayerStack />
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
        <TraceSection />
        <ComparisonSection />
        <PayoffSection />
        <LayersSection />
        <CtaSection />
      </main>
    </Layout>
  );
}
