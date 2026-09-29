---
id: aspire-topology
title: Let Aspire start the topology
sidebar_label: Aspire topology
sidebar_position: 2
description: "Select the OpenCSMS AppHost with one key and let it run the API and both workers as real processes beside fresh containers."
---

import LearnShell from '@site/src/components/LearnShell';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import Link from '@docusaurus/Link';

# Let Aspire start the topology

The container mode kept the product inside the test process. The topology mode hands the product its own processes: an Aspire AppHost starts the API, both workers and the two containers, and the suite follows the addresses it publishes.

<LearnShell
  level="Level 5, lesson 2"
  minutes="About 9 minutes"
  outcome={[
    'Select the AppHost with one key and read what it starts.',
    'Tell which provider serves a target when the AppHost is selected.',
    'Name the gates behind the journeys that skip against real processes.',
  ]}
  before={[
    <>Run the suite on containers (<Link to="/learn/real-topology/containers">lesson 1</Link>).</>,
    'An OpenCSMS checkout and a container runtime. Reading the lesson alone also works.',
  ]}
  situation={
    <>
      <p>The next step after fresh containers is the product's own processes: a real API, a real billing worker, a real notification worker. Aspire is how OpenCSMS declares that topology, and the suite can resolve its targets through it without a second Setup.</p>
      <p>The AppHost stays dormant unless a selection key asks for it. That is what keeps one Setup honest across four modes.</p>
    </>
  }
  checkpoint={{
    question:
      'The topology run is green with 13 skips, and the container run of the same suite has none. Name the two gates behind the skips and what each one needs that this mode does not have.',
    verify: (
      <>
        Read the skipped lines in the topology log, then the conditions on the tests that skipped, such as <code>[RequiresInProcess]</code> and <code>[RequiresTestClock]</code>.
      </>
    ),
    reveal: (
      <>
        <code>[RequiresInProcess]</code> needs the test host: the AppHost runs the product as separate processes, so there is no in-process server, no loopback application and no worker host to reach. <code>[RequiresTestClock]</code> needs the run's clock inside the application; these processes read the machine clock. The runner lists every skipped test; <code>--logger "console;verbosity=detailed"</code> prints each reason beside it.
      </>
    ),
  }}
  learned={[
    'The AppHost is one provider in the same chain, selected by one key.',
    'It runs the API and both workers as project resources beside PostgreSQL and RabbitMQ.',
    'Real processes bring their own clock and no test host, so the clock and in-process journeys skip by name.',
  ]}
  next={[
    {
      label: 'Point the suite at a real stack',
      to: '/learn/real-topology/published-mode',
      note: 'The same idea with the processes started outside the suite, the way a deployment looks.',
    },
    {
      label: 'Aspire',
      to: '/docs/integrations/aspire',
      note: 'The selection keys, the resource map and what each provider publishes.',
    },
  ]}>

## What the AppHost declares

The AppHost project is a small file with no product code. It declares the topology:

- PostgreSQL, with an `opencsms` database, as a container resource.
- RabbitMQ as a container resource.
- The API project, which also serves the dashboard, as a project resource.
- The billing worker and the notification worker as project resources.

The suite's registration maps those resources onto the targets it already has:

<AnnotatedCode
  filename="Setup.cs"
  code={`.AddAspireAppHost<OpenCsmsAppHostAnchor>(
    options => options
        .MapResource("api", CsmsTargets.Api)
        .MapConnectionString("opencsms", CsmsInfrastructureExtensions.ConnectionStringKey)
        .MapConnectionString("rabbitmq", RabbitMqOptions.ConnectionStringSetting),
    "api",
    "opencsms",
    "rabbitmq")`}
  callouts={[
    {line: 1, title: 'Pin the AppHost assembly', note: 'A public anchor type names the assembly, because the AppHost entry point is internal and the testing host runs that entry point in-process.'},
    {line: 3, title: 'The API becomes the application', note: 'MapResource publishes the api resource under the Csms application, so the REST and browser clients follow its address.'},
    {line: 4, title: 'The store and the broker', note: 'MapConnectionString fills the two keys the existing providers also serve, so nothing downstream changes.'},
    {line: 6, title: 'Declare the resources', note: 'The last three names are the resources this registration follows.'},
  ]}
  foot={<>From the suite's <code>Setup.cs</code>. The chain order still holds: a configured address wins over the AppHost.</>}
/>

## Select it, then run it

The run script does exactly one thing to select this mode. It clears the keys a published run would export and sets the global selection key:

```bash
pwsh eng/run-suite.ps1 -Mode topology
```

That sets `ProtoTest__Aspire__Enabled=true`, which is the environment variable form of `ProtoTest:Aspire:Enabled`. Without a selection key the AppHost never starts, and the suite resolves every target through its other providers. A per-resource key, `ProtoTest:Aspire:Resources:{resource}:Enabled`, selects one resource instead of all of them. The [Aspire page](/docs/integrations/aspire) documents both.

The AppHost also steps aside for what the run already provides. A run that exports the store or the broker key keeps it; the AppHost declares its own resource only for a key the environment left unset.

## The run's own counts

```text
Passed!  - Failed:     0, Passed:    61, Skipped:    13, Total:    74, Duration: 20 s - OpenCsms.Suite.dll (net8.0)
```

That is the run recorded on 2026-09-28 in `opencsms-topology-20260928-094052.log`. The 13 skips are the journeys that need the test host or the run's clock; the log lists each one by name, and the condition on the test names the reason. The suite still owns the tests, the fixtures and the evidence. It does not own the product's lifetime any more.

![The station timeline: a charge point, the remote-start panel and the sessions the dashboard lists.](/images/opencsms/station-timeline.png)

The dashboard the API resource serves. The station screen is the operator's view of the sessions the product recorded.

## What this mode cannot do

Two limits are worth knowing before you build an AppHost for a suite:

- The AppHost must target the suite's framework. The testing host runs the AppHost's entry point inside the test process, and the AppHost's orchestrator launches project resources with `dotnet run`, which cannot choose a target framework. OpenCSMS keeps every project on net8.0 for this reason.
- Real processes mean real clocks. A test that moves the run's clock cannot move a process it does not own, so it should declare `[RequiresTestClock]` and skip here instead of failing.

</LearnShell>
