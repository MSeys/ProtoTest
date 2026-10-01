---
id: wait-for-a-lagging-read
title: Wait for a read that lags a write
sidebar_label: Wait for a lagging read
sidebar_position: 4
description: "Poll a read with a deadline when background work updates it, and count the probes in the trace."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';

# Wait for a read that lags a write

<Lesson
  track="Reliable tests"
  step="Lesson 4 of 4"
  minutes={8}
  outcomes={[
    'Poll a read with a deadline instead of sleeping',
    'Turn a poll that never holds into a failure that says what never arrived',
    'Count the probes of a poll in the trace',
  ]}
  needs={[<>The previous lesson, <a href="/learn/reliable-tests/parallel-safety">Parallel safety</a></>]}
/>

## The problem

A write returns, but the read that reports its effect is updated later by background work. Northstar sends webhooks this way. Creating a project queues a delivery, and a dispatcher sends queued deliveries every 100 ms on a real timer.

A test that reads once right after the write can see the delivery still pending. In one measurement on this sample, 9 of 20 immediate reads did. A sleep before the read guesses the lag. It is too short on a loaded agent and wasted time on a fast one.

The [test clock](/learn/reliable-tests/the-test-clock) does not help here. It moves the time the application reads, but not the dispatcher's timer.

## Do it

### 1. Run the test

From the repository root:

```bash
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~WebhookJourney"
```

It should report one passed test.

### 2. Read the arrange steps

The test in `samples/Northstar.ProtoTest/WebhookJourney.cs` creates a webhook sink, subscribes the tenant to `project.created`, and creates a project:

```csharp
var sink = await Proto.Context.Data().For<ConfigureWebhookSinkRequest>().CreateAsync<WebhookSinkResponse>();
using var webhook = await Proto.Context.Rest()
    .Body(new CreateWebhookRequest(sink.Url.ToString(), [WebhookEventTypes.ProjectCreated]))
    .PostAsync("/api/v1/webhooks");
webhook.Should.HaveHttpStatus(HttpStatusCode.Created);

using var created = await Proto.Context.Rest()
    .Body(new CreateProjectRequest($"hooked-{Proto.Context.TestId}"))
    .PostAsync("/api/v1/projects");
created.Should.HaveHttpStatus(HttpStatusCode.Created);
```

The sink is a test-support endpoint inside the sample application. It accepts deliveries without opening a socket.

### 3. Poll the read with a deadline

```csharp
var delivered = await ProtoPolling.PollAsync(
    async cancellationToken =>
    {
        using var page = await Proto.Context.Rest().GetAsync(
            "/api/v1/webhook-deliveries",
            new { status = WebhookDeliveryStatuses.Delivered },
            cancellationToken);
        return page
            .Should.HaveHttpStatus(HttpStatusCode.OK)
            .ReadRequired<CursorPage<WebhookDeliveryResponse>>()
            .Items;
    },
    deliveries => deliveries.Count > 0,
    timeout: TimeSpan.FromSeconds(5),
    pollInterval: TimeSpan.FromMilliseconds(100),
    Proto.Context.CancellationToken);

Assert.That(delivered.Satisfied, Is.True, $"no delivery after {delivered.Elapsed.TotalMilliseconds:F0} ms");
Assert.That(delivered.Value.Single().EventType, Is.EqualTo(WebhookEventTypes.ProjectCreated));
```

`PollAsync` runs the probe at once, then every 100 ms, until the condition holds or 5 seconds pass. It returns the last value it read, whether the condition held, and the time it took.

A timeout does not throw. The first assertion turns it into a failure that says how long the test waited. The second checks what arrived.

The 5 seconds are a ceiling, not a cost. A delivery that arrives in 80 ms ends the wait in 80 ms.

### 4. Count the probes in the trace

Download [l3-lagging-read.prototrace](pathname:///lessons/l3-lagging-read.prototrace) and open it in the [viewer](https://trace.prototest.dev). In the execution phase, find the `REST · GET /api/v1/webhook-deliveries` entries.

The poll writes no entry of its own. Each probe calls the REST client, so each probe is one entry. One entry means the dispatcher had already sent the delivery when the first read arrived. Two or more mean the read came first, and the same test still passed.

### 5. Make it time out

Change the condition to `deliveries => deliveries.Count > 1`, which this test can never meet, and run it again. After the deadline, the first assertion fails with the time it waited:

```text
no delivery after 5008 ms
  Expected: True
  But was:  False
```

That run's trace held 45 `GET /api/v1/webhook-deliveries` entries, one per probe, all answered 200. A test that times out still shows every question it asked. Change the condition back.

## What happened

The test asked the question a user of the API would ask: has the delivery happened? It asked again until the answer was yes or the deadline passed. The wait ended as soon as the answer changed, and the trace shows how many times it asked.

## Check yourself

<Checkpoint
  question="The probe reads deliveries with status=delivered. Why not read every delivery and check that one exists?"
  verify={<>Read the drill <code>OneReadRacesTheDispatcher</code> in <code>WebhookJourney.cs</code>, which reads every delivery.</>}>

A pending delivery exists as soon as the project is created. A condition on "one exists" would pass on the first probe, before anything was sent. The condition has to name the state the test is waiting for.

</Checkpoint>

<Checkpoint
  question="Why not move the test clock forward instead of polling?"
  verify={<>Read the limits of the <a href="/docs/foundation/time#limits">test clock</a>.</>}>

The dispatcher runs on a <code>PeriodicTimer</code>, which runs on real time. Moving the test clock changes what the application reads as now. It does not make the timer fire sooner.

</Checkpoint>

## Remember

- When background work updates a read, poll that read with a deadline instead of sleeping.
- A poll returns its result. The test turns a timeout into a failure that names what never arrived.
- Each probe through a ProtoTest client is one trace entry, so the trace shows how long the wait really was.

## Go deeper

- [Test time](/docs/foundation/time#waiting-for-work-on-a-real-timer): the `PollAsync` contract and what the clock does not move.
- Next track: [Read a failing trace](/learn/understand-failures/read-a-failing-trace).
