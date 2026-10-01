---
id: one-host-one-lifetime
title: Know when the host starts and stops
sidebar_label: One host, one lifetime
sidebar_position: 2
description: "See what the setup class does before the first test and after the last, and why the builder refuses registrations once the host is built."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import Link from '@docusaurus/Link';

# Know when the host starts and stops

<Lesson
  track="Write good integration tests"
  step="Lesson 2 of 7"
  minutes={8}
  outcomes={[
    'Say when the run builds, starts and stops the host',
    'Find where the setup class composes the host',
    'Read the host\'s own releases at the end of a trace',
  ]}
  needs={[
    <>Lesson 1, <Link to="/learn/good-tests/capabilities-and-the-host">Read what your run provides</Link></>,
    'The sample cloned. Reading the archive alone also works',
  ]}
/>

## The problem

Nothing in the sample's tests calls `Setup.Configure`, yet every test finds a database, an application and a trace sink ready. Something starts them before the first test and something stops them after the last.

You need to know who does that, and why a test can never add to the composition.

## Do it

### 1. Find the setup class

The setup class configures the host for a test project. Open `samples/Northstar.ProtoTest/Setup.cs`. It is short at the top because its base class does the work:

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
    {line: 1, title: 'NUnit\'s once-per-assembly hook', note: 'The base class carries the attribute and the setup and teardown methods. Deriving from ProtoTestAssembly is the whole registration.'},
    {line: 4, title: 'You only write Configure', note: 'It receives the builder. A test body never sees the builder, so the composition stays one readable list.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/Setup.cs</code>, trimmed to the declaration and the three composition calls.</>}
/>

### 2. See what the base class does with it

The base class starts the host once before any test and stops it once after the last. This is NUnit's:

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
    {line: 9, title: 'Stop once', note: 'After the last test, the base stops the host and disposes it.'},
  ]}
  foot={<>From <code>src/ProtoTest.NUnit/ProtoTestAssembly.cs</code>. The other runner packages hook into their own runner's start and end, then take the same path below.</>}
/>

Under that, every runner takes the same three steps:

<AnnotatedCode
  filename="ProtoTestHostLifetime.cs"
  code={`var builder = new ProtoHostBuilder();
configure(builder);
var host = builder.Build();
await host.StartAsync().ConfigureAwait(false);`}
  callouts={[
    {line: 2, title: 'Configure composes', note: 'Your Configure adds applications, integrations, sinks, hooks and gates to one builder.'},
    {line: 3, title: 'Build is the last step', note: 'It creates the host. After it, any registration throws: "The ProtoHostBuilder has already built a ProtoHost; configure a new builder instead."'},
    {line: 4, title: 'Start opens the run', note: 'Run hooks run, capabilities are recorded and infrastructure starts. Only then do tests start.'},
  ]}
  foot={<>From <code>src/ProtoTest.Core/ProtoTestHostLifetime.cs</code>, the shared start path behind every runner.</>}
/>

### 3. Look for the end of the run in a trace

Open [l1-first-journey.prototrace](pathname:///lessons/l1-first-journey.prototrace) in the [viewer](https://trace.prototest.dev). The host writes its own work in the run layer, outside every test. At the end, three entries release run resources after the test's teardown:

| Entry | What it releases |
| --- | --- |
| `Release · messaging:broker` | the run's messaging adapter, in memory in this recording |
| `Release · readiness:application:Northstar web` | the readiness check for the web application |
| `Release · application:loopback:Northstar web` | the listener the browser journey follows |

## What happened

The runner called your `Configure` once, built the host, started it, ran the tests, then stopped it. The test never owned the messaging adapter or the listener, so no test teardown could release them. The host did, once, at the end.

Build is the last step on purpose. A registration after it would change a run that already started, so the builder throws. Each test still gets its own context and clients. Those are per-test state, not registrations.

In [l2-broker-skip.prototrace](pathname:///lessons/l2-broker-skip.prototrace) the only selected test skipped. The host still started, recorded its capabilities and released the same three resources.

## Check yourself

<Checkpoint
  question="The first journey's trace lists seven capabilities and three run resources. Who releases the three, and when, relative to the test?"
  verify={<>Open <a href="pathname:///lessons/l1-first-journey.prototrace">l1-first-journey.prototrace</a> and read the run layer: the capabilities at its top, the releases at its end.</>}>

The host releases them, in the run layer, after the test's teardown. The test never owned them, so no test teardown could have released them.

</Checkpoint>

## Remember

- One host per test project: built from `Configure`, started before the first test, stopped after the last.
- A test cannot add to the host. Registrations after `Build()` throw.
- The run layer of a trace records the host's own work: capabilities, resources and releases.

## Go deeper

- [Host and lifecycle](/docs/foundation/lifecycle): the full sequence of run hooks, gates, reports, resources and the archive, and how a failing stop is reported.
- Next: [Add and remove an integration](/learn/good-tests/add-and-remove-an-integration).
