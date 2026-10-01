---
id: containers
title: Run the suite on containers
sidebar_label: Containers
sidebar_position: 1
description: "Run the OpenCSMS suite with a PostgreSQL and a RabbitMQ container the run owns, and read the counts the run reported."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import ModesComparison from '@site/src/components/ModesComparison';

# Run the suite on containers

<Lesson
  track="Real systems"
  step="Lesson 1 of 4"
  minutes={8}
  outcomes={[
    'Run the suite with PostgreSQL and RabbitMQ containers the run owns',
    'Read the chain that decides who provides the database and the broker',
    'Say what the run does when the environment already provides them',
  ]}
  needs={['The Reliable tests track', 'Optional: an OpenCSMS checkout and a container runtime, such as Docker Desktop']}
/>

## The problem

A suite that needs a database and a broker usually asks you to install both first. You would rather start the suite and let it bring what it needs.

These lessons use OpenCSMS, a separate suite whose repository is not public yet. Each lesson is written as a walk through its recorded runs: the committed run logs and traces. You can follow every step by reading them, even without the checkout. The commands and output below are quoted from those records and are not run on this site.

OpenCSMS is an EV charging management system. It has a REST API with a dashboard, PostgreSQL for the store, RabbitMQ for events, a billing worker and a notification worker. Its suite has one setup class and four modes. This track reads three of them, then the faults the suite injects on purpose.

## Do it

### 1. Run the suite in container mode

From the OpenCSMS repository root, with a container runtime available:

```bash
pwsh eng/run-suite.ps1 -Mode container
```

The script builds the dashboard first, runs `dotnet test tests/OpenCsms.Suite -c Release`, and tees the run to `artifacts/gates/opencsms-container-<timestamp>.log`. A plain `dotnet test tests/OpenCsms.Suite` skips the seven Chromium journeys, because the dashboard build they wait for is the script's first step.

### 2. Read the run's own counts

The log ends with the suite's summary:

```text
Passed!  - Failed:     0, Passed:    75, Skipped:     0, Total:    75, Duration: 6 s - OpenCsms.Suite.dll (net8.0)
```

This is the run recorded on 2026-09-28 in `opencsms-container-20260928-194709.log`. All 75 tests ran, including the seven Chromium journeys, the OCPP device journeys and the showpiece journey that once caught the idle fee regression. Container mode is the full suite, not a smoke run.

### 3. Read the chain that decided

The setup class declares the database and the broker the same way. Each is a chain of providers, and the first provider whose condition holds serves it:

<AnnotatedCode
  filename="Setup.cs"
  code={`.AddInfrastructure(
    "CsmsDatabase",
    chain => chain
        .UseConfigured()
        .UseAspireResource<OpenCsmsAppHostAnchor>("opencsms")
        .UseContainer(PostgresDatabase.Container()),
    CsmsInfrastructureExtensions.ConnectionStringKey)
.AddInfrastructure(
    "CsmsBroker",
    chain => chain
        .UseConfigured()
        .UseAspireResource<OpenCsmsAppHostAnchor>("rabbitmq")
        .UseContainer(RabbitMqBroker.Container()),
    RabbitMqOptions.ConnectionStringSetting,
    "Messaging:RabbitMq:ConnectionString")`}
  callouts={[
    {line: 4, title: 'A configured address wins', note: 'The environment provides the connection string, so no container starts for that target. That is what the configured mode relies on.'},
    {line: 5, title: 'Then the AppHost', note: 'The next lesson selects this provider. It stays second, so a configured address still wins over it.'},
    {line: 6, title: 'Then a container the run owns', note: 'PostgresDatabase.Container() starts postgres:16-alpine only when nothing above it served the key. The broker chain follows with rabbitmq:3.'},
    {line: 15, title: 'One key, two readers', note: 'The last argument names the setting the winning provider fills. The application and the suite read one address, not two.'},
  ]}
  foot={<>From the suite's <code>Setup.cs</code>.</>}
/>

Nothing is configured on a clean machine, so the chain reaches `UseContainer`. The run starts the two containers and removes them when it ends.

![The OpenCSMS stations screen: three stations with their charge points, connector counts and last-seen stamps.](/images/opencsms/dashboard.png)

The operator dashboard on a local run. The seven Chromium journeys drive this screen, served by the API the run hosts.

## What happened

Container mode is one picture:

- **The application** runs inside the test process: the API and both workers, started by the host.
- **The database and broker** are containers the run starts and removes, `postgres:16-alpine` and `rabbitmq:3`.
- **What the suite skips**: nothing. All 75 tests ran.

The suite is the same in every mode. Only the winner of each chain changes. If the environment already provides a database and a broker, the chains step aside. A machine without a container runtime can export these three keys, and no container starts:

```
ConnectionStrings__Csms
Messaging__RabbitMq__ConnectionString
ProtoTest__Messaging__RabbitMq__ConnectionString
```

The first is the product's database. The second is the product's broker. The third is the address the suite's own messaging tap uses.

This is how the three modes compare. The next two lessons explain the other two rows.

<ModesComparison />

## Check yourself

<Checkpoint
  question="The container run is green and starts both containers. You export ConnectionStrings:Csms before the next run. What happens to the CsmsDatabase container, and where is that decided?"
  verify={<>Read the CsmsDatabase chain in the suite's Setup.cs, then say its first provider out loud.</>}>

`UseConfigured()` is first. The target resolves from the configured connection string, the container provider never runs, and no container starts. The same suite then runs against the store you provided, which is what the configured mode does with one persistent database.

</Checkpoint>

## Remember

- Each target follows an ordered chain, and the first provider whose condition holds serves it.
- Container mode runs the application in the test process and starts `postgres:16-alpine` and `rabbitmq:3` for the run.
- The run writes its own log, so the mode evidence is a file rather than a claim.

Next: [Let Aspire start the topology](/learn/real-systems/aspire-topology).

## Go deeper

- [Environments](/docs/getting-started/environments): configured addresses, containers and deployed applications in one suite.
