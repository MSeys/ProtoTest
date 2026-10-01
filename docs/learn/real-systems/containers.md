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
  needs={[
    'The Reliable tests track',
    'To run the commands: an OpenCSMS checkout with its .NET tooling, PowerShell, Node.js/npm and a container runtime',
  ]}
/>

## The problem

A suite that needs a database and a broker usually asks you to install both first. You would rather start the suite and let it bring what it needs.

These lessons use recorded runs from OpenCSMS, a separate suite. You can follow the explanation and quoted evidence without its checkout. Running the commands requires the checkout and its development tools.

OpenCSMS manages EV charging. It has a REST API with a dashboard, PostgreSQL for storage, RabbitMQ for events, and billing and notification workers. The dashboard is the operator's interface. Its suite has one setup class and four modes. This track reads three modes, then examines failures the suite creates deliberately.

## Do it

### 1. Run the suite in container mode

From the OpenCSMS repository root, with a container runtime available:

```bash
pwsh eng/run-suite.ps1 -Mode container
```

The script first builds the dashboard with npm. It then runs `dotnet test tests/OpenCsms.Suite -c Release` and saves the test output to `artifacts/gates/opencsms-container-<timestamp>.log`.

A plain `dotnet test tests/OpenCsms.Suite` does not build that dashboard. Its browser journeys skip when the built `dist/index.html` is absent. An existing build can let them run.

### 2. Read the run's own counts

The recorded log ends with this summary:

```text
Passed!  - Failed:     0, Passed:    75, Skipped:     0, Total:    75, Duration: 6 s - OpenCsms.Suite.dll (net8.0)
```

This run was recorded on 2026-09-28 in `opencsms-container-20260928-194709.log`. All 75 tests ran, including the seven Chromium journeys, OCPP device journeys and the regression journey for idle fees.
These counts describe that recording. Your checkout and configuration can produce different counts.

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
    {line: 4, title: 'Configured values win', note: 'UseConfigured requires a nonempty value for every key declared by this target. The database declares one key; the broker declares two.'},
    {line: 5, title: 'Then the AppHost', note: 'The next lesson selects this provider. It stays second, so a configured address still wins over it.'},
    {line: 6, title: 'Then a container the run owns', note: 'The host starts postgres:16-alpine when the container provider wins. The broker chain uses rabbitmq:3.'},
    {line: 15, title: 'Two keys for one broker', note: 'The final two arguments declare the broker settings. One is for the suite and one for the application. Both need values for UseConfigured to win.'},
  ]}
  foot={<>From the suite's <code>Setup.cs</code>.</>}
/>

With no configured connection strings and Aspire unselected, both chains reach `UseContainer`. The host starts the containers and disposes them at the end.
The script clears Aspire selection for container mode, but keeps exported connection strings and application addresses. Check those values before interpreting the mode name as proof of what started.

![The OpenCSMS stations screen: three stations with their charge points, connector counts and last-seen stamps.](/images/opencsms/dashboard.png)

The dashboard on a local run. The Chromium journeys drive this screen, served by the API the run hosts.

## What happened

In the recorded container run:

- **The application** runs inside the test process: the API and both workers, started by the host.
- **The database and broker** are containers the run starts and removes, `postgres:16-alpine` and `rabbitmq:3`.
- **Tests**: all 75 passed, with no skips in this recording.

The same setup class supports configured services. Set these environment variables to connection strings for an existing database and broker:

```
ConnectionStrings__Csms
Messaging__RabbitMq__ConnectionString
ProtoTest__Messaging__RabbitMq__ConnectionString
```

The first is the product's database. The second is the product's broker. The third is the address the suite's own messaging tap uses. Point both broker settings at the same broker.

Run the script with `-Mode configured` to require all three exported values before testing. Both configured providers then win, so these chains start no containers.
Using `-Mode container` keeps the same exported values, but does not require them. Missing values can therefore cause a chain to select a container.

The table compares the recorded runs used in these lessons. The next two lessons explain the other two rows.

<ModesComparison />

## Check yourself

<Checkpoint
  question="You set ConnectionStrings__Csms to an existing database's connection string before running the script again. Which provider serves CsmsDatabase, and what happens to CsmsBroker?"
  verify={<>Read the CsmsDatabase chain in the suite's Setup.cs, then say its first provider out loud.</>}>

`UseConfigured()` is first in the database chain. Its one declared key has a value, so that chain uses the existing database and starts no PostgreSQL container.
The broker chain resolves independently. Without both broker settings, it still falls back to a RabbitMQ container when Aspire is unselected.

</Checkpoint>

## Remember

- Each target follows an ordered chain, and the first provider whose condition holds serves it.
- Without configured services or Aspire selection, the fallback providers start `postgres:16-alpine` and `rabbitmq:3` for the run.
- The script writes the test output to a log. Read that run's results alongside its configuration.

Next: [Let Aspire start the topology](/learn/real-systems/aspire-topology).

## Go deeper

- [Environments](/docs/getting-started/environments): configured addresses, containers and deployed applications in one suite.
