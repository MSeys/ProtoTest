import type {ReactNode} from 'react';
import Link from '@docusaurus/Link';
import Layout from '@theme/Layout';
import Heading from '@theme/Heading';

import TabbedCode, {type CodeTab} from '@site/src/components/TabbedCode';
import Comparison from '@site/src/components/Comparison';
import {withoutProtoTest, withProtoTest} from '@site/src/data/comparison';
import CoverageMap from '@site/src/components/CoverageMap';
import TraceView from '@site/src/components/TraceView';
import LayerStack from '@site/src/components/LayerStack';
import styles from './index.module.css';

const heroTabs: CodeTab[] = [
  {
    id: 'rest',
    label: 'REST',
    filename: 'PlatformLifecycleTests.cs',
    code: `[ProtoTest]
[SampleUser(SampleRoles.TenantAdministrator)]
public async Task Control_plane_reports_the_rollup()
{
    using var response = await Proto.Context.Rest()
        .GetAsync("/api/control-plane");

    response
        .ShouldHaveStatus(HttpStatusCode.OK)
        .ShouldMatchShape(new
        {
            workspaceCount = 1,
            releaseCount = 1,
            monthlyRecurringRevenue =
                JsonValue.GreaterThan(400m)
        });
}`,
    footnote:
      'Shapes match partially and recursively — declare only the properties the behaviour depends on.',
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

var admin = Proto.Context.Context<SampleUserContext>();

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
    <header className={styles.hero}>
      <div className="container">
        <div className={styles.heroInner}>
          <div>
            <div className={styles.eyebrow}>Composable integration testing for .NET</div>
            <Heading as="h1" className={styles.heroTitle}>
              Every layer.
              <br />
              One test context.
            </Heading>
            <p className={styles.heroLead}>
              REST, GraphQL, the browser and your data layer aren't four testing tools bolted
              together. In ProtoTest they're four clients on one shared execution context — with one
              lifecycle, one set of attributes, and one trace covering all of it.
            </p>
            <div className={styles.heroButtons}>
              <Link
                className={`button button--lg ${styles.btnPrimary}`}
                to="/docs/getting-started/installation">
                Get started
              </Link>
              <Link
                className={`button button--lg ${styles.btnGhost}`}
                to="https://github.com/MSeys/ProtoTest">
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

function ComparisonSection() {
  return (
    <section className={styles.section}>
      <div className="container">
        <div className={styles.sectionHead}>
          <Heading as="h2">Most of an integration test isn't the test.</Heading>
          <p>
            The same scenario, the same assertions, the same in-process application. Dimmed lines
            are the ones that exist only to make the test possible — plumbing you write, own, and
            eventually debug.
          </p>
        </div>
        <Comparison without={withoutProtoTest} with={withProtoTest} />
        <p className={styles.honestyNote}>
          Count the files, not just the test: the ProtoTest version has <em>more</em> code written
          once. The setup didn't disappear — it moved into attributes like{' '}
          <code>[SampleEnvironment]</code> and <code>[SampleUser]</code>, which every other fixture
          now reuses for the cost of a line. And that one-time <code>Setup.cs</code> is also what
          gives you the trace, the contract coverage and the report — none of which the left-hand
          version has at all.
        </p>
      </div>
    </section>
  );
}

function PayoffSection() {
  return (
    <section className={`${styles.section} ${styles.sectionAlt}`}>
      <div className="container">
        <div className={styles.sectionHead}>
          <Heading as="h2">Pass or fail is the least interesting thing a run can tell you.</Heading>
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
            <Heading as="h3">When something breaks, the run explains itself.</Heading>
            <p>
              Every hook, client, request, assertion and artifact is recorded as a phased operation
              tree — automatically, with no logging code in your tests. A failure stays attached to
              the operation that produced it.
            </p>
            <p>
              The whole run exports as one portable <code>.prototrace</code> file you can open in
              the viewer, locally or from CI.
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

function LayersSection() {
  return (
    <section className={styles.section}>
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
          <div className={styles.heroButtons} style={{justifyContent: 'center'}}>
            <Link
              className={`button button--lg ${styles.btnPrimary}`}
              to="/docs/getting-started/first-test">
              Write your first test
            </Link>
            <Link
              className={`button button--lg ${styles.btnGhost}`}
              to="https://github.com/MSeys/ProtoTest">
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
      description="ProtoTest is a composable integration-testing foundation for .NET: REST, GraphQL, browser and data testing as clients on one shared execution context, with automatic tracing and contract coverage.">
      <Hero />
      <main>
        <ComparisonSection />
        <PayoffSection />
        <LayersSection />
        <CtaSection />
      </main>
    </Layout>
  );
}
