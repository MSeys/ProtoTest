---
id: published-mode
title: Point the suite at a real stack
sidebar_label: Published mode
sidebar_position: 3
description: "Run the OpenCSMS API and both workers as real processes, hand the suite their addresses, and read what it skips."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';

# Point the suite at a real stack

<Lesson
  track="Real systems"
  step="Lesson 3 of 4"
  minutes={8}
  outcomes={[
    'Start the product as real processes and point the suite at them with configuration',
    'Name the keys that switch the same setup class off the test host',
    'Say what a worker does when its target is not configured',
  ]}
  needs={['Lesson 2, Let Aspire start the topology', 'Optional: an OpenCSMS checkout and a container runtime']}
/>

## The problem

A deployed environment is a set of addresses plus a store, and both outlive the test process. The suite should not care which machine answers, only that the addresses resolve.

In topology mode the AppHost still started the processes. Here they start outside the suite, the way a deployment looks, and the suite only reads addresses. OpenCSMS rehearses that on one machine. As before, the steps walk through the recorded run.

## Do it

### 1. Start the two containers once

They keep their data across runs:

```bash
docker run -d --name opencsms-postgres -e POSTGRES_USER=opencsms -e POSTGRES_PASSWORD=opencsms -e POSTGRES_DB=opencsms -p 5432:5432 postgres:16-alpine
docker run -d --name opencsms-rabbitmq -p 5672:5672 rabbitmq:3-alpine
```

### 2. Run the mode

```bash
pwsh eng/run-suite.ps1 -Mode published
```

The script builds the product and starts `OpenCsms.Api.dll` on `http://127.0.0.1:5080`. It starts the billing worker and the notification worker, then waits until `/healthz` answers. It exports five keys, runs the suite with `--no-build`, and stops all three processes again.

### 3. Read the five keys

| Key | What it points at |
| --- | --- |
| `ConnectionStrings__Csms` | the product's PostgreSQL database |
| `Messaging__RabbitMq__ConnectionString` | the product's RabbitMQ broker |
| `ProtoTest__Messaging__RabbitMq__ConnectionString` | the address the suite's own messaging tap uses |
| `ProtoTest__Applications__Csms__BaseUrl` | the API the suite calls and provisions through |
| `ProtoTest__Applications__Dashboard__BaseUrl` | the same address, for the browser session |

These keys are the whole switch. `UseConfigured()` is first in every chain, so the in-process server and the worker hosts never start, and the environment runs the workers instead. No test contains a condition for this.

### 4. Read the counts

```text
Passed!  - Failed:     0, Passed:    61, Skipped:    13, Total:    74, Duration: 20 s - OpenCsms.Suite.dll (net8.0)
```

This is the run recorded on 2026-09-28 in `opencsms-published-20260928-090106.log`. The skips are the same clock-gated and in-process-gated journeys as in the topology mode. The log lists each one by name, and the condition on the test names the reason.

### 5. Read the worker logs beside it

The process logs tell the other half of the story. The billing worker consumed the real broker:

```text
SessionEndedConsumer consuming 'billing.session-ended'.
```

The notification worker had no targets and said so, once per consumer:

```text
InvoiceIssuedNotificationConsumer is idle: No invoice-ready target is configured ('Notifications:InvoiceReadyBaseUrl').
BillingFailedNotificationConsumer is idle: No billing-failure target is configured ('Notifications:BillingFailureBaseUrl').
```

![An invoice detail: energy amount, start fee, idle fee and the total the billing worker calculated.](/images/opencsms/invoice.png)

The invoice detail the real worker stored: energy, start fee and idle fee, summed into the total.

![The invoices screen with the monthly export: a month picker and a download button.](/images/opencsms/export.png)

The monthly export the suite downloads in Chromium. The file is a real `.xlsx` the API composes from the stored invoice rows.

## What happened

Published mode is one picture:

- **The application** runs as processes started outside the suite, which only reads their addresses.
- **The database and broker** are persistent containers the run points at. The suite starts nothing.
- **What the suite skips**: the same 13 journeys as topology mode.

A worker with no target does not fail. It starts, idles, and prints the named reason. That is a recorded fact, not a hidden failure.

The mode is local, and it has limits:

- There is no staging target. The processes run against containers on the same machine and stop when the suite ends.
- A real process cannot reach the suite's fakes, which live inside the test process, so those journeys skip.
- The clock-gated journeys skip, because these processes read the machine clock.

A team with a real environment exports the same five keys and runs `dotnet test`. The suite does not change.

## Check yourself

<Checkpoint
  question="The published run starts a real notification worker and the worker does nothing. Which keys are missing, and what does each consumer print instead?"
  verify={<>Read the notification worker's log beside the suite log, then the two target keys in the suite's Setup.</>}>

Both notification targets are unset. Each consumer starts and idles with a named reason: no invoice-ready target and no billing-failure target. That is what a local rehearsal looks like. The suite's WireMock fakes live inside the test process, and a real worker process cannot reach them.

</Checkpoint>

## Remember

- Five configuration keys move the same suite from the test host to a running stack.
- The environment runs the workers. The suite starts nothing and skips the journeys it cannot serve.
- An idle worker with a named reason is a recorded fact, not a hidden failure.

Next: [Inject faults on purpose](/learn/real-systems/fault-injection).

## Go deeper

- [Environment resolution](/docs/foundation/environment-resolution): how a configured address wins the chain and what each provider publishes.
