---
id: containers
title: Run the suite on containers
sidebar_label: Containers
sidebar_position: 1
description: "Run the OpenCSMS suite with a PostgreSQL and a RabbitMQ container the run owns, and read the counts the run reported."
---

import LearnShell from '@site/src/components/LearnShell';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import ModesComparison from '@site/src/components/ModesComparison';
import Link from '@docusaurus/Link';

# Run the suite on containers

Your suite needs a database and a broker, and you would rather not install either. Level 5 runs OpenCSMS, an EV charging system in its own repository. OpenCSMS is the suite that uses real infrastructure. Its suite has one Setup and four modes, and this level reads three of them plus the faults the suite injects on purpose.

<LearnShell
  level="Level 5, lesson 1"
  minutes="About 15 minutes"
  outcome={[
    'Run the suite with PostgreSQL and RabbitMQ containers the run owns.',
    'Read the composition to see which provider serves the store and the broker.',
    'Say what the run does when the environment already provides them.',
  ]}
  before={[
    <>Level 4 (<Link to="/learn/evidence/evidence-in-ci">take the evidence to CI</Link>).</>,
    'An OpenCSMS checkout and a container runtime (Docker Desktop or equivalent). Reading the lesson alone also works.',
  ]}
  situation={
    <>
      <p>A suite that needs a database and a broker usually asks you to install both. OpenCSMS takes the other route: each target declares an ordered provider chain, and the first provider whose condition holds serves it.</p>
      <p>The last link in the chain is a container the run starts and removes. One Setup runs in every mode; the environment decides which link wins.</p>
    </>
  }
  checkpoint={{
    question:
      'The container run is green and starts both containers. You export ConnectionStrings:Csms before the next run. What happens to the CsmsDatabase container, and where is that decided?',
    verify: (
      <>
        Read the CsmsDatabase chain in the suite's Setup.cs, then read the chain's first provider out loud.
      </>
    ),
    reveal: (
      <>
        UseConfigured() is first. The target resolves from the configured connection string, the container provider never runs, and no container starts. The same suite then runs against the store you provided, which is what the configured mode does with one persistent database.
      </>
    ),
  }}
  learned={[
    'Each target follows an ordered chain, and the first provider whose condition holds serves it.',
    'Container mode starts postgres:16-alpine and rabbitmq:3 for the run and removes them with it.',
    'The run writes its own log, so the mode evidence is a file rather than a claim.',
  ]}
  next={[
    {
      label: 'Let Aspire start the topology',
      to: '/learn/real-topology/aspire-topology',
      note: 'The next provider in the same chain: an AppHost that runs the product\'s own processes.',
    },
    {
      label: 'Environments',
      to: '/docs/getting-started/environments',
      note: 'Configured addresses, containers and deployed applications in one suite.',
    },
  ]}>

## What the suite needs

OpenCSMS is an EV charging management system with a real product shape:

- A REST API with a dashboard, an OCPP gateway for charge points and per-tenant API keys.
- PostgreSQL for the store and RabbitMQ for events.
- A billing worker that turns an ended session into an invoice, and a notification worker that pushes invoices to external targets.

Container mode runs the API and both workers inside the test process, the way the Northstar sample does, and starts the two containers the run owns. The suite is the same in every mode; only the winners of the provider chains change.

## Run it

From the OpenCSMS repository root, with a container runtime available:

```bash
pwsh eng/run-suite.ps1 -Mode container
```

The script builds the dashboard first, runs `dotnet test tests/OpenCsms.Suite -c Release`, and tees the run to `artifacts/gates/opencsms-container-<timestamp>.log`. Run the script for the count below. A plain `dotnet test tests/OpenCsms.Suite` skips the seven Chromium journeys, because the dashboard build they wait for is the script's first step, so it does not reproduce that summary.

## The chain that decides

The store and the broker are declared the same way, one provider after another:

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
  foot={<>From the suite's <code>Setup.cs</code>. <code>UseConfigured</code>, <code>UseAspireResource</code> and <code>UseContainer</code> are the three providers this level explains.</>}
/>

## The run's own counts

The log the script wrote ends with the suite's own summary:

```text
Passed!  - Failed:     0, Passed:    75, Skipped:     0, Total:    75, Duration: 6 s - OpenCsms.Suite.dll (net8.0)
```

That is the run recorded on 2026-09-28 in `opencsms-container-20260928-194709.log`. All 75 tests ran, including the seven Chromium journeys, the OCPP device journeys and the showpiece journey that once caught the idle fee regression. Container mode is the full suite, not a smoke run.

How the three modes compare, with the counts each lesson quotes:

<ModesComparison />

![The OpenCSMS stations screen: three stations with their charge points, connector counts and last-seen stamps.](/images/opencsms/dashboard.png)

The operator dashboard on a local run. This is the stations screen the seven Chromium journeys drive, served by the API the run hosts.

![The public network status page: each charge point with its connector states and no account needed.](/images/opencsms/status.png)

The public status page is the anonymous read the suite checks next to the operator surface, with no sign-in.

## When the environment provides the pieces

A machine without a container runtime can still run the suite. Provide the three keys the configured mode requires, and the chains step aside:

```
ConnectionStrings__Csms
Messaging__RabbitMq__ConnectionString
ProtoTest__Messaging__RabbitMq__ConnectionString
```

The first key is the product's database, the second the product's broker, and the third the address the suite's own messaging tap uses. The same suite then runs against the environment you point it at, and the run log tells the story again.

</LearnShell>
