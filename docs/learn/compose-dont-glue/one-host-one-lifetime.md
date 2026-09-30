---
id: one-host-one-lifetime
title: One host, one lifetime
sidebar_label: One host, one lifetime
sidebar_position: 2
description: "How the sample's Setup builds one host for the whole run, what starts and stops it, and why Build() is terminal."
---

import LearnShell from '@site/src/components/LearnShell';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import Link from '@docusaurus/Link';

# One host, one lifetime

The run builds the host once from `Setup.cs`. This lesson opens it: what the base class does before the first test and after the last, and the line the builder refuses to cross.

<LearnShell
  level="Level 2, lesson 2"
  minutes="About 8 minutes"
  outcome={[
    'Say when the run\'s host is built, started and stopped.',
    'Read the run layer of a trace as the host\'s own record.',
    'Tell what Configure may still change and what Build() has closed off.',
  ]}
  before={[
    <>Capabilities and the host (<Link to="/learn/compose-dont-glue/capabilities-and-the-host">lesson 1</Link>).</>,
    'The sample cloned. Reading the archives alone also works.',
  ]}
  situation={
    <>
      <p>The sample composes everything in one method, <code>Setup.Configure</code>, and no test calls it. The runner calls it once per assembly, before any test runs, and stops the host once after the last one. Both ends belong to the base class, not to the suite.</p>
      <p>That single lifetime is why a capability, a container or a report sink is a run decision. It is also why the builder has a last step: after <code>Build()</code>, a registration would mutate a live run instead of composing one, so the builder refuses it.</p>
    </>
  }
  checkpoint={{
    question:
      'The first journey\'s archive lists seven capabilities and three run resources. Who releases the three, and when does that happen relative to the test?',
    verify: (
      <>
        Download <a href="pathname:///lessons/l1-first-journey.prototrace">l1-first-journey.prototrace</a>, open it in the{' '}
        <a href="https://trace.prototest.dev">viewer</a>, and read the run layer: the capabilities at its top, the releases at its end.
      </>
    ),
    reveal: (
      <>
        The host releases them, in the run's own layer: <code>Release · messaging:broker</code>, <code>Release · readiness:application:Northstar web</code> and <code>Release · application:loopback:Northstar web</code>, after the test's teardown. The test never owned them, so no test teardown could have released them.
      </>
    ),
  }}
  learned={[
    'One host per test assembly: built from Configure once, started before the first test, stopped after the last.',
    'Build() is terminal; every registration after it throws instead of changing a live run.',
    'The run layer of a trace records the host\'s own work: capabilities, resources, gates and releases.',
  ]}
  next={[
    {
      label: 'Add and remove an integration',
      to: '/learn/compose-dont-glue/add-and-remove-an-integration',
      note: 'Watch a run piece come and go, and the capability list follow it.',
    },
    {
      label: 'Host and lifecycle',
      to: '/docs/foundation/lifecycle',
      note: 'The full sequence: run hooks, gates, reports, resources and the archive.',
    },
  ]}>

## The class the runner calls

`samples/Northstar.ProtoTest/Setup.cs` is short at the top because the base class does most of the work:

<AnnotatedCode
  filename="Setup.cs"
  code={`[SetUpFixture]
public sealed class Setup : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder)
    {
        var configuration = LoadConfiguration();
        var run = NorthstarRun.From(configuration);
        run.PrepareOwnedStore();

        ConfigureInfrastructure(builder, run);
        ConfigureApplications(builder, run);
        ConfigureDomain(builder, run);
        // ... tracing, sinks, the run gate and messaging
    }
}`}
  callouts={[
    {line: 1, title: 'NUnit\'s once-per-assembly hook', note: 'The base class carries the attribute, so deriving from ProtoTestAssembly is the whole registration. The other runners have their own assembly hook.'},
    {line: 2, title: 'One class, one run', note: 'Everything the run shares is written here, once.'},
    {line: 4, title: 'Called before the first test', note: 'Configure receives the builder; a test body can never add to it. That is what makes the composition readable as one list.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/Setup.cs</code>, trimmed to the declaration and the three composition calls.</>}
/>

The base class is the same idea for every runner. This is NUnit's:

<AnnotatedCode
  filename="ProtoTestAssembly.cs"
  code={`[SetUpFixture]
public abstract class ProtoTestAssembly
    : ProtoTestAssemblyHost<ProtoTestAssembly>, IProtoTestAssemblyHost<ProtoTestAssembly>
{
    [OneTimeSetUp]
    public Task GlobalSetUp() => StartAsync(Configure);

    [OneTimeTearDown]
    public Task GlobalTearDown() => StopAsync();

    protected abstract void Configure(IProtoHostBuilder builder);
}`}
  callouts={[
    {line: 6, title: 'Start once', note: 'Before any test, the base builds the host from Configure and starts it.'},
    {line: 9, title: 'Stop once', note: 'After the last test, the base stops the host and disposes it. Both calls throw if the lifetime already ran, so a second start cannot reopen the run.'},
  ]}
  foot={<>From <code>src/ProtoTest.NUnit/ProtoTestAssembly.cs</code>. The xUnit, xUnit v3, TUnit and MSTest packages ship the same base under their own assembly hooks.</>}
/>

## Configure, then Build, then start

The base class hides three steps that every runner takes in the same order:

<AnnotatedCode
  filename="ProtoTestHostLifetime.cs"
  code={`var builder = new ProtoHostBuilder();
configure(builder);
var host = builder.Build();
await host.StartAsync().ConfigureAwait(false);`}
  callouts={[
    {line: 2, title: 'Configure composes', note: 'Your Setup.Configure adds applications, integrations, sinks, hooks and gates to one builder.'},
    {line: 3, title: 'Build is the terminal step', note: 'It validates the composition and creates the host. After it, any registration throws: "The ProtoHostBuilder has already built a ProtoHost; configure a new builder instead."'},
    {line: 4, title: 'Start opens the run', note: 'Run hooks run, capabilities are recorded, infrastructure starts, the trace listener attaches. Only then can the first test start.'},
  ]}
  foot={<>From <code>src/ProtoTest.Core/ProtoTestHostLifetime.cs</code>, the shared start path behind every adapter.</>}
/>

The terminal rule blocks a second registration. It would change a run that already started. A second `Build()` throws the same message, and a second start throws `ProtoHost has already been initialized for this assembly.` The rule applies to every entry on the builder, from `ConfigureServices` to `AddRunGate`.

## The run's two ends in the trace

The host records its own work in the trace's run layer, outside every test. `l1-first-journey.prototrace` holds both ends. The two timestamps are the ones the archive records, and both are UTC: the run's own start and the test's setup entry.

| Record | Reading |
| --- | --- |
| Seven `capability` entities: Playwright, Data, Sheets, GraphQL, REST, ASP.NET Core, SQL | recorded when the host started, before the test's setup opened |
| Run start `18:37:08.072Z`, the test's setup opens `18:37:09.060Z` | the host was alive about 1.0 second before the test |
| `Release · messaging:broker`, 0.3 ms | the host releases a run piece after the test |
| `Release · readiness:application:Northstar web`, 0.0 ms | the readiness probe is a run resource |
| `Release · application:loopback:Northstar web`, 5.0 ms | the listener the browser journey follows |
| The report's Resources section lists the run pieces as `Registered` | the report is written before the releases, so its snapshot says so |

A run with no tests still has that shape. `l2-broker-skip.prototrace` holds only the three releases and no test resource at all, because the broker journey skipped before its lifecycle started. The host existed, served the capability list, and was stopped exactly once.

The state entities in the same archive carry their own stamps with a local offset, so the viewer prints two clocks in one run. Compare the two timestamps above with each other, and read a duration rather than a wall time when you compare an entity with a span.

## One host, not one per test

`ProtoTestAssembly.Host` is the static host a `[ProtoTest]` test resolves, and its lifetime belongs to the closed generic type, so two adapter assemblies loaded in one process each own their host instead of sharing one. When the run ends, stopping clears it: a later lookup reports that the host was not initialized and repeats the runner's own hint, such as `Ensure your setup class inherits from ProtoTestAssembly.` The next run in the same process starts a fresh host.

That is the whole contract: compose in one place, build once, start once, stop once. Everything else in this level is a decision about what goes into that one host.

</LearnShell>
