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

A deployed environment can outlive the test process. You want the suite to use its existing API, database and broker rather than start replacements.

In topology mode, the suite started the product through Aspire. Here a script starts the product processes outside the suite and supplies their addresses. OpenCSMS rehearses that arrangement on one machine.

This lesson explains the saved run. To run the commands yourself, use the optional OpenCSMS checkout and a container runtime.

## Do it

### 1. Start the two containers once

The rehearsal expects these named containers to be running before the suite starts:

```bash
docker run -d --name opencsms-postgres -e POSTGRES_USER=opencsms -e POSTGRES_PASSWORD=opencsms -e POSTGRES_DB=opencsms -p 5432:5432 postgres:16-alpine
docker run -d --name opencsms-rabbitmq -p 5672:5672 rabbitmq:3-alpine
```

The script reuses the same containers across suite runs and leaves them running afterwards. These commands do not configure named volumes or guarantee data recovery after container recreation.

### 2. Run the mode

From the OpenCSMS repository root:

```bash
pwsh eng/run-suite.ps1 -Mode published
```

The script builds the dashboard and product, then starts `OpenCsms.Api.dll` on `http://127.0.0.1:5080`. It starts both workers and waits until `/healthz` returns HTTP 200.

It supplies the five keys below, runs the suite with `--no-build`, and stops the three product processes afterwards. This is a local deployment rehearsal, not a staging environment.

### 3. Read the five keys

| Key | What it points at |
| --- | --- |
| `ConnectionStrings__Csms` | the product's PostgreSQL database |
| `Messaging__RabbitMq__ConnectionString` | the product's RabbitMQ broker |
| `ProtoTest__Messaging__RabbitMq__ConnectionString` | the address the suite's own messaging tap uses |
| `ProtoTest__Applications__Csms__BaseUrl` | the API the suite calls and provisions through |
| `ProtoTest__Applications__Dashboard__BaseUrl` | the same address, for the browser session |

The script also clears Aspire selectors, the seed override and both notification-target environment keys. That prevents those shell settings from changing this rehearsal's configuration.

`UseConfigured()` comes first in the relevant application and infrastructure chains. The supplied addresses replace the suite's API, dashboard, database and broker providers. The API's worker registrations follow that choice, so ProtoTest does not host those workers in the test process.

The setup class still registers local HTTP fakes and other test resources. Selecting configured product addresses does not disable the rest of the suite.

### 4. Read the counts

```text
Passed!  - Failed:     0, Passed:    61, Skipped:    13, Total:    74, Duration: 20 s - OpenCsms.Suite.dll (net8.0)
```

This output comes from `artifacts/gates/opencsms-published-20260928-090106.log` in the OpenCSMS checkout. It lists the same 13 skipped names as the topology recording, without printing their reasons.

The source explains their requirements: seven clock-dependent journeys, two outbox substitutions and four notification journeys requiring in-process services, hosted workers and the clock.

This snapshot contains 74 tests. Lesson 1's later container snapshot contains 75, including the added tariff-repricing journey. That journey is absent from this log, not an additional skip. Do not expect identical totals across different source snapshots.

### 5. Read the worker logs beside it

The process logs sit beside the suite log, with `billing-worker` and `notification-worker` in their filenames. The billing log confirms that its consumer started on the broker queue:

```text
SessionEndedConsumer consuming 'billing.session-ended'.
```

The notification process had no target addresses. Each of its two consumers reported why it remained idle:

```text
InvoiceIssuedNotificationConsumer is idle: No invoice-ready target is configured ('Notifications:InvoiceReadyBaseUrl').
BillingFailedNotificationConsumer is idle: No billing-failure target is configured ('Notifications:BillingFailureBaseUrl').
```

![An invoice detail: energy amount, start fee, idle fee and the total the billing worker calculated.](/images/opencsms/invoice.png)

The product's invoice detail shows energy, start fee and idle fee, summed into the total. This screenshot illustrates the screen, not evidence from the quoted run.

![The invoices screen with the monthly export: a month picker and a download button.](/images/opencsms/export.png)

The monthly export screen offers an `.xlsx` download of stored invoice rows. Its browser download journey is one of the skips in this published recording.

## What happened

In this published rehearsal:

- **The product** runs in three processes that the script starts and stops outside the test process.
- **The database and broker** run in existing containers that outlive the suite invocation.
- **The suite** still owns clients, fixtures, local HTTP fakes and reports. The recorded run skips the same 13 tests as the topology recording.

An idle notification consumer does not prove delivery works. These consumers deliberately stop before subscribing when their target address is missing. Their log messages explain why this run does not exercise outbound notifications.

The mode has these limits:

- The separate processes do not receive the suite's in-process service substitutions or clock bridge.
- WireMock listens on HTTP ports, which another local process can reach when configured with their addresses. This script does not supply those addresses to the notification process.
- Notification journeys also declare in-process, hosted-worker and clock requirements. A reachable fake alone does not satisfy those requirements.

A team can supply the five keys for another compatible environment and run `dotnet test` with Aspire selection disabled. Capability requirements still determine which journeys can run there.

## Check yourself

<Checkpoint
  question="The published run starts a real notification worker and the worker does nothing. Which keys are missing, and what does each consumer print instead?"
  verify={<>Read the notification log beside the suite log. Compare it with the target keys cleared by <code>eng/run-suite.ps1</code> in published mode.</>}>

`Notifications:InvoiceReadyBaseUrl` and `Notifications:BillingFailureBaseUrl` are unset in the notification process. The invoice consumer prints the invoice-ready message above. The failure consumer prints the billing-failure message.

The suite configures its own local fakes, but that configuration does not automatically reach an already-started worker process. HTTP reachability and process configuration are separate concerns.

</Checkpoint>

## Remember

- Configured addresses let the suite use an existing product stack. The script also clears conflicting mode settings.
- The script owns the product processes. The suite still owns its test resources and checks capability requirements.
- An idle notification consumer explains missing configuration. It does not demonstrate successful notification delivery.

Next: [Inject faults on purpose](/learn/real-systems/fault-injection).

## Go deeper

- [Environment resolution](/docs/foundation/environment-resolution): how a configured address wins the chain and what each provider publishes.
