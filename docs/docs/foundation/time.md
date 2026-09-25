---
sidebar_position: 11
title: Test time
description: "Advance a clock instead of sleeping: each test gets its own TimeProvider, the in-process application sees it, and every advance is recorded in the trace."
---

# Test time

Tariffs, expiry, idle fees and retention rules are time-dependent, and a test that sleeps through them is slow and flaky. ProtoTest gives every test a clock you move by hand, and the application under test reads the same clock.

## Seeding the run

```csharp
builder.ConfigureClock(new ProtoClock(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero)));
```

Every test's clock starts where the run clock points, so tests begin from the same fixed instant. Without this call the run starts at the current real time.

## Advancing a test

```csharp
[ProtoTest]
public async Task An_overstaying_car_is_charged_an_idle_fee()
{
    var session = await StartSessionAsync(energyKwh: 22);

    Proto.Context.Clock.Advance(TimeSpan.FromHours(5));

    using var invoice = await Proto.Context.Rest().GetAsync($"/api/invoices/{session.InvoiceId}");
    invoice.ShouldMatchShape(new { idleFee = JsonValue.GreaterThan(0) });
}
```

`Proto.Context.Clock` is this test's `ProtoClock`: `Advance(delta)` moves it forward, `SetUtcNow(instant)` moves it anywhere. Each test starts from the run's seed, so advancing time in one test never leaks into another - parallel tests keep separate timelines.

## What the application sees

An in-process [ASP.NET Core application](../integrations/aspnetcore.md) has its `TimeProvider` replaced with the run's, and each request carries the id of the test that caused it, so application code that injects `TimeProvider` sees exactly the time the test set - without knowing about ProtoTest:

```csharp
public sealed class TariffService(TimeProvider timeProvider)
{
    public decimal Calculate(...) => ... timeProvider.GetUtcNow() ...;
}
```

Two things must be true for this to work: the application resolves `TimeProvider` from dependency injection (not `DateTime.UtcNow`, and not a cached `TimeProvider.System`), and the request comes from the test's own client.

A [worker host](../integrations/hosting.md) runs on background flows with no test, so it sees the run clock. Advance the run clock to move a worker's time:

```csharp
ProtoHost.CurrentHost.Clock.Advance(TimeSpan.FromDays(1));
```

## What the trace records

Advancing a test's clock records a `clock` entity with its new value and writes a `clock.advance` event carrying the delta and both instants; advancing the run clock records the same event on the run. [Trace entries](../observability/prototrace.md) need tracing enabled - a suite that turned entries off keeps the entity state only.

## Limits

- **Only `GetUtcNow` is virtual.** Timers created from this provider still run on real time; code that schedules with `TimeProvider.CreateTimer` or `PeriodicTimer` is not accelerated.
- **Direct wall-clock calls are not affected.** `DateTime.UtcNow`, `DateTimeOffset.UtcNow` and `Environment.TickCount` bypass the clock. Application code must read its `TimeProvider`.
- **Published environments cannot be faked.** A deployed process keeps its own time, so time-dependent journeys are in-process journeys - guard them with `[RequiresInProcess]` or a capability skip.
- **An application that caches time fails the same way it would in production.** A service that resolves the clock once and stores a value it computed earlier stays stale; the fake clock makes that visible rather than causing it.
- **The clock is per test, the run is per suite.** Advancing `Proto.Context.Clock` does not touch infrastructure or workers; use `ProtoHost.CurrentHost.Clock` when the whole run should move.
