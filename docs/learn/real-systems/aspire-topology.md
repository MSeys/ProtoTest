---
id: aspire-topology
title: Let Aspire start the topology
sidebar_label: Aspire topology
sidebar_position: 2
description: "Select the OpenCSMS AppHost with one key and let it run the API and both workers as real processes beside fresh containers."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';
import AnnotatedCode from '@site/src/components/AnnotatedCode';

# Let Aspire start the topology

<Lesson
  track="Real systems"
  step="Lesson 2 of 4"
  minutes={8}
  outcomes={[
    'Select the AppHost with one key and read what it starts',
    'Tell which provider serves a target when the AppHost is selected',
    'Name the gates behind the journeys that skip against real processes',
  ]}
  needs={['Lesson 1, Run the suite on containers', 'Optional: an OpenCSMS checkout and a container runtime']}
/>

## The problem

Container mode kept the product inside the test process. That is convenient, but the real product is separate processes: an API, a billing worker and a notification worker. You want the suite to run against those, started the way the product starts them.

OpenCSMS declares that topology in Aspire, in a project called the AppHost. As in lesson 1, the steps below walk through the recorded run, not a live one.

## Do it

### 1. Read what the AppHost declares

The AppHost project is a small file with no product code. It declares:

- PostgreSQL, with an `opencsms` database, as a container resource.
- RabbitMQ as a container resource.
- The API project, which also serves the dashboard, as a project resource.
- The billing worker and the notification worker as project resources.

### 2. Map its resources onto the suite's targets

The suite's registration says which resource fills which target:

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

### 3. Select the mode

The run script does one thing to select this mode. It clears the keys a published run would export and sets the global selection key:

```bash
pwsh eng/run-suite.ps1 -Mode topology
```

That sets `ProtoTest__Aspire__Enabled=true`, the environment variable form of `ProtoTest:Aspire:Enabled`. Without a selection key the AppHost never starts, and the suite resolves every target through its other providers. A per-resource key, `ProtoTest:Aspire:Resources:{resource}:Enabled`, selects one resource instead of all of them.

The AppHost also steps aside for what the run already provides. A run that exports the store or the broker key keeps it. The AppHost declares its own resource only for a key the environment left unset.

### 4. Read the counts

```text
Passed!  - Failed:     0, Passed:    61, Skipped:    13, Total:    74, Duration: 20 s - OpenCsms.Suite.dll (net8.0)
```

This is the run recorded on 2026-09-28 in `opencsms-topology-20260928-094052.log`. The log lists each of the 13 skips by name, and the condition on each test names the reason.

![The station timeline: a charge point, the remote-start panel and the sessions the dashboard lists.](/images/opencsms/station-timeline.png)

The dashboard the API resource serves. The station screen is the operator's view of the sessions the product recorded.

## What happened

Topology mode is one picture:

- **The application** runs as real processes: the API and both workers, started by the AppHost.
- **The database and broker** are containers the AppHost starts, fresh for this run.
- **What the suite skips**: 13 journeys. They need the test host or the run's clock, and real processes have neither.

The suite still owns the tests, the fixtures and the trace. It no longer owns the product's lifetime.

Two attributes gate the skips. A test marked `[RequiresInProcess]` needs the application inside the test process. A test marked `[RequiresTestClock]` needs the run's clock to reach the application. Here the processes read the machine clock, so a test that moves the run's clock cannot move them. Such a test declares `[RequiresTestClock]` and skips instead of failing.

:::note What this mode cannot do

The AppHost must target the suite's framework. The testing host runs the AppHost's entry point inside the test process, and the AppHost launches project resources with `dotnet run`, which cannot choose a target framework. OpenCSMS keeps every project on net8.0 for this reason.

:::

## Check yourself

<Checkpoint
  question="The topology run is green with 13 skips, and the container run of the same suite has none. Name the two gates behind the skips and what each one needs that this mode does not have."
  verify={<>Read the skipped lines in the topology log, then the conditions on the tests that skipped, such as <code>[RequiresInProcess]</code> and <code>[RequiresTestClock]</code>.</>}>

`[RequiresInProcess]` needs the test host. The AppHost runs the product as separate processes, so there is no in-process server, no loopback application and no worker host to reach. `[RequiresTestClock]` needs the run's clock inside the application. These processes read the machine clock. The runner lists every skipped test, and `--logger "console;verbosity=detailed"` prints each reason beside it.

</Checkpoint>

## Remember

- The AppHost is one provider in the same chain, selected by one key.
- It runs the API and both workers as project resources beside PostgreSQL and RabbitMQ.
- Real processes bring their own clock and no test host, so the clock and in-process journeys skip by name.

Next: [Point the suite at a real stack](/learn/real-systems/published-mode).

## Go deeper

- [Aspire](/docs/integrations/aspire): the selection keys, the resource map and what each provider publishes.
