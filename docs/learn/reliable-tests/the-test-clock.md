---
id: the-test-clock
title: Move the test clock
sidebar_label: Move the test clock
sidebar_position: 1
description: "Replace a test that waits on real time with one that moves the clock, and read the clock event in the trace."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';

# Move the test clock

<Lesson
  track="Reliable tests"
  step="Lesson 1 of 3"
  minutes={7}
  outcomes={[
    'Move a test clock past a billing boundary',
    'Explain how an in-process request reads the test clock',
    'Find the clock event and the period close in the trace',
  ]}
  needs={['The sample cloned, or the trace archive linked below']}
/>

## The problem

Northstar issues an invoice when a request finds that a subscription's billing period has ended. Testing that boundary requires the application to read a later time.

```csharp
// Waiting on real time does not move this sample's test clock.
await Task.Delay(TimeSpan.FromSeconds(2));
```

This delay waits two real seconds, but the sample's test clock stays where it was. Increasing the delay would not cross the billing boundary.

Instead, the test moves its clock past the period end and sends a request. The application reads that time and issues the invoice.

## Do it

### 1. Let the application read the test's clock

Use the sample's default local configuration for this lesson. Follow `ClosingTheBillingPeriodIssuesTheInvoiceOnTheTestClock` in `samples/Northstar.ProtoTest/ClockJourney.cs`, or inspect the linked recording.

The setup class starts the API inside the test process. Inside its `AddApplication` registration, this excerpt adds the in-process server:

```csharp
app.AddAspNetCoreServer<NorthstarProgram>(configureWebHost: webHost =>
    ConfigureHostedApplication(webHost, run));
```

This code is in `samples/Northstar.ProtoTest/Setup.cs`. The sample already configures it. The server supplies a `TimeProvider` that reads the current test's clock while handling its requests.

Northstar's billing code uses that provider. Code that reads `DateTimeOffset.UtcNow` directly would still see real time.

### 2. Ask the application where the period ends

```csharp
using var subscription = await Proto.Context.Rest().GetAsync("/api/v1/subscription");
var current = subscription
    .Should.HaveHttpStatus(HttpStatusCode.OK)
    .ReadRequired<SubscriptionResponse>();
```

The response supplies `CurrentPeriodEndUtc`. The test uses that boundary rather than assuming how many days the month contains.

### 3. Move the clock just past that moment

```csharp
// Move the test clock past the period end; the application reads the same clock.
Proto.Context.Clock.Advance(
    current.CurrentPeriodEndUtc - Proto.Context.Clock.GetUtcNow() + TimeSpan.FromSeconds(1));
```

The delta is the time left in the period plus one second, so the clock lands beyond the boundary. `Advance` updates the clock without waiting for that duration to pass.

### 4. Read what the application did

```csharp
using var invoices = await Proto.Context.Rest()
    .GetAsync("/api/v1/invoices", new { status = InvoiceStatuses.Open });
var invoice = invoices
    .Should.HaveHttpStatus(HttpStatusCode.OK)
    .ReadRequired<CursorPage<InvoiceResponse>>()
    .Items
    .Single();
```

This request makes Northstar evaluate the period boundary and issue the invoice. Advancing the clock alone did not execute that billing logic.

The test expects exactly one open invoice. It then checks `IssuedAtUtc` against `current.CurrentPeriodEndUtc`, rather than the advanced time one second later. It also checks the open status and a line description containing `Growth`.

These NUnit assertions are in `samples/Northstar.ProtoTest/ClockJourney.cs`.

### 5. Open the trace

To run the journey yourself, use this command from the repository root:

```bash
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~ClosingTheBillingPeriodIssuesTheInvoiceOnTheTestClock"
```

Expect one passed test in the default local configuration. Its trace appears under `samples/Northstar.ProtoTest/bin/Debug/net8.0/TestResults/`.

Or download [l3-clock-window.prototrace](pathname:///lessons/l3-clock-window.prototrace) and open it in the
[viewer](https://trace.prototest.dev). Select `ClosingTheBillingPeriodIssuesTheInvoiceOnTheTestClock`. The supplied recording contains:

| Entry | What it shows |
| --- | --- |
| `clock.advance` on `test.execution`, "Clock advanced by 30:0:00:01" | the delta, from the test side |
| `http.request` REST `GET /api/v1/subscription`, 141.9 ms, HTTP 200 | the period the test read |
| `Northstar.Domain` `invoice.issue`, reported by the application | the application closed the period |
| `http.request` REST `GET /api/v1/invoices`, 68.4 ms, HTTP 200 | the invoice the test read |
| `test.execution`, 233.7 ms | the test body, excluding setup and teardown |

The recorded test body took 233.7 ms while advancing its clock by a month and one second. Your run's timings can differ.

## What happened

The trace records the clock advance as an event on `test.execution`. The test updates an in-memory clock. The next request lets the application observe the new time.

Each test gets a new clock seeded from the run clock. Advancing one test's clock does not advance another's. The in-process server uses the request's test id to find that clock in its owning host.

The billing check no longer depends on how much real time elapsed. Requests and database work still take real time, so this does not eliminate every possible timeout or failure.

The clock only changes `TimeProvider.GetUtcNow()`. Timers and ordinary delays still use real time. Code outside the test or request flow reads the run clock instead.

The bridge follows the async flow. A background task that captures the request flow can retain its clock, so do not assume all background work uses run time.

This sample's loopback browser application runs separately and does not receive the in-process clock bridge. Container and deployed applications likewise need their own way to control time.

## Check yourself

<Checkpoint
  question="The clock advance in the trace reads 30:0:00:01. Why does the test compute the delta from the period end instead of advancing a fixed number of days?"
  verify={<>Select the test in the archive and find the <code>clock.advance</code> event on the test execution.</>}>

The application supplies the period end, so the test does not assume a month length. Moving one second past it makes the next invoice request cross the boundary.

That request issues the invoice. The assertion compares `IssuedAtUtc` with the original period end, not with the advanced clock value.

</Checkpoint>

## Remember

- Each test has its own clock. The in-process request reads it through `TimeProvider`.
- Advancing the clock does not wait for the requested duration. The trace records the delta.
- The next request triggers Northstar's billing logic. Sleeping does not move this test clock.

## Go deeper

- [Test time](/docs/foundation/time): the clock API, the bridge into the application, and the attributes that skip a clock-dependent test.
- Next lesson: [Wait for readiness, not for time](/learn/reliable-tests/readiness-instead-of-sleeps).
