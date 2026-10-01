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

Container mode hosted the API and workers inside the test process, with a separate loopback application for the browser. Now you want to test the API and both workers as separate processes.

OpenCSMS declares those processes and their dependencies in an Aspire AppHost project. This lesson explains the saved run and the configuration that selected it. Running the command yourself requires the optional OpenCSMS checkout and a container runtime.

## Do it

### 1. Read what the AppHost declares

Read `Program.cs` in the OpenCSMS AppHost project. It declares these resources:

- PostgreSQL, with an `opencsms` database, when `ConnectionStrings:Csms` is unset.
- RabbitMQ when `Messaging:RabbitMq:ConnectionString` is unset.
- The API project, which also serves the dashboard, as a project resource.
- The billing worker and the notification worker as project resources.

The API and worker resources always appear in this graph. Each receives either the configured database and broker addresses or the addresses of containers the AppHost starts.

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
    {line: 1, title: 'Identify the AppHost assembly', note: 'The public anchor type identifies the assembly. Aspire runs its entry point inside the test process, then starts the declared project resources.'},
    {line: 3, title: 'Map the API address', note: 'MapResource maps the api endpoint to the Csms application. The separate Dashboard provider chain also selects the api resource for browser requests.'},
    {line: 4, title: 'Map the store and broker', note: 'The two MapConnectionString calls publish connection strings under keys the suite already uses.'},
    {line: 6, title: 'Name the mapped resources', note: 'These three names identify resources whose settings ProtoTest can publish. They do not restrict the AppHost graph to three resources.'},
  ]}
  foot={<>From the suite's <code>Setup.cs</code>. The chain order still holds: a configured address wins over the AppHost.</>}
/>

### 3. Select the mode

From the OpenCSMS repository root, this command selects topology mode:

```bash
pwsh eng/run-suite.ps1 -Mode topology
```

The script clears the five environment keys used by published mode: the database address, both RabbitMQ addresses and both application base URLs. It also clears existing global and per-resource Aspire selectors.

It then sets `ProtoTest__Aspire__Enabled=true`, the environment variable form of `ProtoTest:Aspire:Enabled`, builds the dashboard and runs the suite. Without an Aspire selection key, this registration does not start the AppHost.

A per-resource key, `ProtoTest:Aspire:Resources:{resource}:Enabled`, selects which resource's settings ProtoTest uses. It does not remove other resources from the AppHost graph.

Configured addresses still take precedence in the provider chains. Outside this script's cleared environment, the AppHost can use a configured store or broker instead of declaring that container.

### 4. Read the counts

```text
Passed!  - Failed:     0, Passed:    61, Skipped:    13, Total:    74, Duration: 20 s - OpenCsms.Suite.dll (net8.0)
```

This output comes from `artifacts/gates/opencsms-topology-20260928-094052.log` in the OpenCSMS checkout. It lists all 13 skipped test names, but does not print their reasons.

This snapshot contains 74 tests. Lesson 1's later container recording contains 75, including the added tariff-repricing journey. That additional journey is absent from this log, not counted as skipped. These recordings do not compare identical test sets.

![The station timeline: a charge point, the remote-start panel and the sessions the dashboard lists.](/images/opencsms/station-timeline.png)

The dashboard the API resource serves. The station screen is the operator's view of the sessions the product recorded.

## What happened

In the recorded topology run:

- **The application** runs as real processes: the API and both workers, started by the AppHost.
- **The database and broker** come from containers the AppHost starts for the run.
- **The suite** still owns fixtures, local HTTP fakes, clients and the trace.

The suite owns the AppHost's lifetime. Aspire manages the product processes, and ProtoTest stops the AppHost when it releases the run's resources.

The skipped names fall into three groups. Their source declares the capabilities each test needs:

| Group in the saved log | Requirement missing in this mode |
| --- | --- |
| Seven clock-dependent journeys, including the browser invoice export | `[RequiresTestClock]`: the suite's clock must control the application |
| Two outbox retry journeys | `[RequiresInProcess]`: they replace the API's event publisher through its services |
| Four notification delivery and outage journeys | In-process application and hosted workers, plus the test clock |

Some tests also declare `[RequiresWorker<T>]`, which checks for a worker hosted by ProtoTest. A separate worker process does not provide that capability. The saved log does not establish which missing requirement the adapter reported first.

:::note What this mode cannot do

OpenCSMS targets its suite, AppHost and product projects at net8.0. Aspire loads the AppHost entry point inside the test process. Its project launcher does not choose a target framework for multi-targeted project resources, so those resources use a single target.

:::

## Check yourself

<Checkpoint
  question="Why do the outbox retry and clock-dependent journeys skip even though the API and workers are running?"
  verify={<>Compare the skipped names with <code>OutboxTests</code> and the clock-dependent journeys. Read their capability attributes, not only the pass count.</>}>

`[RequiresInProcess]` requires an application server inside the test process. The outbox tests need it to substitute the event publisher. `[RequiresTestClock]` requires the suite's clock to control application time. These separate processes use their own clocks.

Several journeys also require ProtoTest-hosted workers. Running an equivalent worker outside the test process does not grant access to its services or clock.

</Checkpoint>

## Remember

- The AppHost is one provider in the same chain, selected by one key.
- It runs the API and both workers as project resources beside PostgreSQL and RabbitMQ.
- Separate processes do not provide ProtoTest's in-process services or clock. Capability attributes keep those journeys from running in an unsupported mode.

Next: [Point the suite at a real stack](/learn/real-systems/published-mode).

## Go deeper

- [Aspire](/docs/integrations/aspire): the selection keys, the resource map and what each provider publishes.
