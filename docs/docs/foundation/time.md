---
sidebar_position: 16
title: Test time
description: "Advance a clock instead of sleeping: each test gets its own TimeProvider, the in-process application sees it, and every advance is recorded in the trace."
---

# Test time

Some behavior depends on time, for example tariffs and expiry. A test that sleeps through that time is slow and flaky. ProtoTest gives every test a clock you move by hand, and the application under test reads the same clock.

## Seeding the run

```csharp
builder.ConfigureClock(new ProtoClock(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero)));
```

Every test's clock starts where the run clock points, so tests begin from the same fixed instant. Without this call the run starts at the current real time.

## Advancing a test

```csharp
[ProtoTest]
public async Task AnOverstayingCarIsChargedAnIdleFee()
{
    var session = await StartSessionAsync(energyKwh: 22);

    Proto.Context.Clock.Advance(TimeSpan.FromHours(5));

    using var invoice = await Proto.Context.Rest().GetAsync($"/api/invoices/{session.InvoiceId}");
    invoice.Should.MatchShape(new { idleFee = JsonValue.GreaterThan(0) });
}
```

`Proto.Context.Clock` is this test's `ProtoClock`. `Advance(delta)` moves it forward, and `SetUtcNow(instant)` moves it anywhere. Each test starts from the run's seed, so advancing time in one test never leaks into another, and parallel tests keep separate timelines.

| Clock | Moves when |
| --- | --- |
| `Proto.Context.Clock` | the test advances it with `Advance` or `SetUtcNow` |
| `ProtoHost.CurrentHost.Clock` | the run advances it. Workers with no test see this clock. |

## What the application sees

An in-process [ASP.NET Core application](../integrations/aspnetcore.md) has its `TimeProvider` replaced with the run's, and each request carries the id of the test that caused it. Application code that injects `TimeProvider` then sees exactly the time the test set, without knowing about ProtoTest:

```csharp
public sealed class TariffService(TimeProvider timeProvider)
{
    public decimal Calculate(...) => ... timeProvider.GetUtcNow() ...;
}
```

Two things must be true for this to work. The application resolves `TimeProvider` from dependency injection, not `DateTime.UtcNow` and not a cached `TimeProvider.System`. And the request comes from the test's own client. The lookup is scoped to the host that owns the test, so two hosts configured with the same `RunPrefix` each link their requests to their own clock.

A [worker host](../integrations/hosting.md) runs on background flows with no test, so it sees the run clock. Advance the run clock to move a worker's time:

```csharp
ProtoHost.CurrentHost.Clock.Advance(TimeSpan.FromDays(1));
```

The bridge exists only where the run hosts the process: the in-process application server and a hosted worker. A [loopback](../integrations/aspnetcore.md#choosing-how-the-application-runs), container, AppHost or published application resolves its own `TimeProvider`, so the run cannot move its time. Otherwise `[RequiresTestClock]` skips the journeys that need it.

## Waiting for work on a real timer

Some work runs on its own timer, such as a background dispatcher or an outbox worker. Moving the test clock does not run it sooner. Poll the read that reports the work, with a deadline, instead of sleeping:

```csharp
var delivered = await ProtoPolling.PollAsync(
    async cancellationToken =>
    {
        using var page = await Proto.Context.Rest().GetAsync(
            "/api/v1/webhook-deliveries",
            new { status = WebhookDeliveryStatuses.Delivered },
            cancellationToken);
        return page.ReadRequired<CursorPage<WebhookDeliveryResponse>>().Items;
    },
    deliveries => deliveries.Count > 0,
    timeout: TimeSpan.FromSeconds(5),
    pollInterval: TimeSpan.FromMilliseconds(100),
    Proto.Context.CancellationToken);

Assert.That(delivered.Satisfied, Is.True);
```

`PollAsync` lives in `ProtoTest.Core`:

```csharp
public static ValueTask<ProtoPollResult<T>> PollAsync<T>(
    Func<CancellationToken, ValueTask<T>> probe,
    Func<T, bool> isSatisfied,
    TimeSpan timeout,
    TimeSpan pollInterval,
    CancellationToken cancellationToken);

public readonly record struct ProtoPollResult<T>(T Value, bool Satisfied, TimeSpan Elapsed);
```

- The first probe runs at once. The poll returns as soon as `isSatisfied` holds, so a fast system ends the wait early.
- After the timeout, the poll returns the last observation with `Satisfied = false`. It does not throw. The test decides what a timeout means, usually with an assertion that names what never arrived.
- The wait between probes is `pollInterval`, or the time left when that is shorter. `ProtoPolling.DefaultInterval` is 50 ms.
- `timeout` and `pollInterval` must be positive, or the call throws `ArgumentOutOfRangeException`. Cancelling the token throws `OperationCanceledException`.
- An exception from the probe ends the poll and propagates. Catch it inside the probe if a failed read means "not yet".
- `Elapsed` and the deadline use real time, not the test clock.
- The poll writes no trace entry of its own. A probe that calls a ProtoTest client writes that call's entry, so the trace shows one entry per probe.

The lesson [Wait for a read that lags a write](/learn/reliable-tests/wait-for-a-lagging-read) walks through this test.

## What the trace records

Advancing a test's clock records a `clock` entity with its new value and writes a `clock.advance` event carrying the delta and both instants. Advancing the run clock records the same event on the run. [Trace entries](../observability/prototrace.md) need tracing enabled. A suite that turned entries off keeps the entity state only.

## Limits

- **Only `GetUtcNow` is virtual.** Timers created from this provider still run on real time. Code that schedules with `TimeProvider.CreateTimer` or `PeriodicTimer` is not accelerated. [Poll](#waiting-for-work-on-a-real-timer) for its result instead.
- **Direct wall-clock calls are not affected.** `DateTime.UtcNow`, `DateTimeOffset.UtcNow` and `Environment.TickCount` bypass the clock. Application code must read its `TimeProvider`.
- **Published environments cannot be faked.** A deployed process keeps its own time, so time-dependent journeys are in-process journeys. Guard them with `[RequiresTestClock]` (the `clock` capability the winning in-process provider declares), `[RequiresInProcess]` or a capability skip.
- **An application that caches time fails the same way it would in production.** A service that resolves the clock once and stores a value it computed earlier stays stale. The fake clock makes that visible rather than causing it.
- **A pushed clock outlives its request in flows that captured it.** `ProtoRequestClock.Push` restores the previous clock when its scope ends, but it cannot revoke the value from a task that captured it. A fire-and-forget task started inside a request keeps the finished test's clock. Long-lived background work must read the run clock.
- **The clock is per test, the run is per suite.** Advancing `Proto.Context.Clock` does not touch infrastructure or workers. Use `ProtoHost.CurrentHost.Clock` when the whole run should move.
