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
    'Move a test clock instead of waiting on real time',
    'Explain how the in-process application reads the same clock',
    'Find the clock event and the period close in the trace',
  ]}
  needs={['The sample cloned, or the trace archive linked below']}
/>

## The problem

A subscription renews at the end of its billing period. To test the renewal, you have to be past that moment.
The usual answer is a test that waits.

```csharp
// A flaky version: it shortens the period and sleeps through it.
await Task.Delay(TimeSpan.FromSeconds(2));
```

This test is slow on a good day. On a loaded machine the two seconds are not enough, and the test fails for no
reason you can see. A real billing period of a month cannot be waited out at all.

The fix is to stop waiting. The test owns a clock, the application reads that clock, and the test moves it.

## Do it

### 1. Let the application read the test's clock

The sample starts its API inside the test process. That in-process server is what hands the test clock to the
application:

```csharp
builder.AddApplication(NorthstarTargets.Api, app =>
{
    if (run.RunsLocalApplications)
    {
        // The in-process server is what carries the test clock into the application.
        app.AddAspNetCoreServer<NorthstarProgram>(configureWebHost: webHost =>
            ConfigureHostedApplication(webHost, run));
    }
```

The server replaces the application's time provider with the run's clock. Every stamp and period the application
computes now comes from the test clock. This code is in `samples/Northstar.ProtoTest/Setup.cs`.

### 2. Ask the application where the period ends

```csharp
using var subscription = await Proto.Context.Rest().GetAsync("/api/v1/subscription");
var current = subscription
    .Should.HaveHttpStatus(HttpStatusCode.OK)
    .ReadRequired<SubscriptionResponse>();
```

The test reads the period end from the application. It does not assume a month length, so the test keeps working
if the billing rule changes.

### 3. Move the clock just past that moment

```csharp
// Move the test clock past the period end; the application reads the same clock.
Proto.Context.Clock.Advance(
    current.CurrentPeriodEndUtc - Proto.Context.Clock.GetUtcNow() + TimeSpan.FromSeconds(1));
```

The delta is the time left in the period plus one second, so the clock lands just past the boundary. The call
returns at once.

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

The application issued the invoice when the period closed. The test reads the open one and asserts on its stamp
and line with ordinary NUnit assertions. The steps come from `samples/Northstar.ProtoTest/ClockJourney.cs`.

### 5. Open the trace

Download [l3-clock-window.prototrace](pathname:///lessons/l3-clock-window.prototrace) and open it in the
[viewer](https://trace.prototest.dev). Select the test. You should see:

| Entry | What it shows |
| --- | --- |
| `clock.advance` on `test.execution`, "Clock advanced by 30:0:00:01" | the delta, from the test side |
| `http.request` REST `GET /api/v1/subscription`, 141.9 ms, HTTP 200 | the period the test read |
| `Northstar.Domain` `invoice.issue`, reported by the application | the application closed the period |
| `http.request` REST `GET /api/v1/invoices`, 68.4 ms, HTTP 200 | the invoice the test read |
| `test.execution`, 233.7 ms | the whole journey |

The whole month took 233.7 ms.

## What happened

The clock advance is an event on the test execution, not a request. Nothing was sent anywhere to move time. The
test holds the clock, and the in-process application reads it.

That is why the test is reliable. Real time is not part of the test, so a slow machine cannot change the result.
A sleep changes nothing the application can see, because the application only reads its clock.

One limit matters. A loopback, container, AppHost or deployed application resolves its own time provider, and the
run cannot move it. A journey that needs the clock is an in-process journey. The sample runs the API in-process for
these tests and points the browser journey at a loopback instance.

## Check yourself

<Checkpoint
  question="The clock advance in the trace reads 30:0:00:01. Why does the test compute the delta from the period end instead of advancing a fixed number of days?"
  verify={<>Select the test in the archive and find the <code>clock.advance</code> event on the test execution.</>}>

The period end belongs to the application's billing logic, so the test reads it instead of assuming a month
length. One second past that instant closes the period. The assertion that follows compares the invoice stamp with
the same moment the application computed.

</Checkpoint>

## Remember

- The test owns the clock. The in-process application reads it through its time provider.
- Moving the clock is instant, and the trace records the delta.
- Waiting on real time changes nothing the application can see.

## Go deeper

- [Test time](/docs/foundation/time): the clock API, the bridge into the application, and the attributes that skip a clock-dependent test.
- Next lesson: [Wait for readiness, not for time](/learn/reliable-tests/readiness-instead-of-sleeps).
