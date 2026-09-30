---
sidebar_position: 11
title: Hooks
description: "Run code around every test or around the whole run without touching a test: correlation ids, shared resets, one-off startup."
---

# Hooks

## What it is

A hook runs code around **every** test or around the whole run, without any test mentioning it. Use a hook for behavior that applies to every test, for example setting a correlation id. When only some tests need the behavior, write an [attribute](./attributes.md) instead.

```text
hook or attribute?
  applies to every test, tests must not know  ->  hook
  applies to some tests, the test declares it  ->  attribute
```

## How it works

### Test hooks

```csharp
public interface IProtoTestHook
{
    int Order => 0;
    Task BeforeTestAsync(ProtoExecutionContext context) => Task.CompletedTask;
    Task AfterTestAsync(ProtoExecutionContext context) => Task.CompletedTask;
}
```

Both methods have default implementations, so implement only what you need.

```csharp
public sealed class ResetMailboxHook(IMailbox mailbox) : IProtoTestHook
{
    public Task BeforeTestAsync(ProtoExecutionContext context) => mailbox.ClearAsync(context.TestId);
}
```

Test hooks are registered as **singletons**, and constructor parameters are resolved from the root container. For per-test services, resolve them from `context.Services` inside the method.

### Run hooks

```csharp
public interface IProtoRunHook
{
    int Order => 0;
    Task BeforeRunAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task AfterRunAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
```

```csharp
public sealed class StartDependenciesHook(IDependencyStarter starter) : IProtoRunHook
{
    public Task BeforeRunAsync(CancellationToken cancellationToken = default) =>
        starter.StartAsync(cancellationToken);

    public Task AfterRunAsync(CancellationToken cancellationToken = default) =>
        starter.StopAsync(cancellationToken);
}
```

Run hooks run once, before the first test and after the last. They do not receive a context: there is no test yet. For run-scoped pieces that own or start something, prefer [infrastructure](./infrastructure.md), which starts at a defined position and releases with the run.

### Ordering

```text
in (-1000 ... First ... Auth) -> ATTRIBUTES -> out (Auth ... First ... -1000)
```

| | Before | After |
| --- | --- | --- |
| Test hooks | ascending `Order` | descending |
| Run hooks | ascending `Order` | descending |

Lower orders run earlier on the way in and later on the way out, so a hook with `Order = -1_000` wraps everything with a higher order.

ProtoTest's built-in hooks sit at the extremes on purpose, and `ProtoHookOrder` names their positions so a hook can sit relative to them without spelling out raw values:

| Hook | `Order` | Why |
| --- | --- | --- |
| Client initializer (test) | `ProtoHookOrder.First` | runs first on the way in, so your hooks can use clients |
| Client completion (test) | `ProtoHookOrder.ClientCompletion` | completes clients after every other test hook, before disposal |
| Trace export (run) | `ProtoHookOrder.First` | runs last on the way out, after reports and resources |
| Run resources (run) | `ProtoHookOrder.RunResources` | releases run-scoped resources before the trace archive is written |
| Report sinks (run) | `ProtoHookOrder.ReportSinks` | exports reports before resources are released, so the report is a snapshot of the run |
| Run gates (run) | `ProtoHookOrder.RunGates` | evaluates first on the way out, before reports export |
| HTTP auth (test) | `ProtoHookOrder.Authentication` | applies `[Auth<T>]` after your hooks, so it can override the request |

### Reserved `Order` bands

ProtoTest's own attributes and integrations use the ranges below. Put a capability in the band it belongs to instead of guessing a number. Ties inside a band keep registration order.

| Band | `Order` | For |
| --- | --- | --- |
| Infrastructure | `-300` to `-201` | pieces that must exist before environment selection (containers, servers) |
| Environment | `-200` to `-101` | tenant, database, broker and endpoint selection |
| Identity | `-100` to `-1` | users, authentication and roles |
| Scenario | `0` and above | the test's own attributes and hooks (`ProtoHookOrder.Default`) |

Remember that **all test hooks run before any attribute**. See [Host and lifecycle](./lifecycle.md) for the full sequence and failure rules.

## How to use it

Register each hook once on the host builder:

```csharp
builder.AddTestHook<ResetMailboxHook>();
builder.AddRunHook<StartDependenciesHook>();
```

A fuller test hook, from the sample suite (condensed). `NorthstarScenarioHook` gives every test a correlation id, records observations, and attaches a summary:

```csharp
public sealed class NorthstarScenarioHook : IProtoTestHook
{
    public int Order => -1_000;

    public Task BeforeTestAsync(ProtoExecutionContext context)
    {
        var scenario = new NorthstarScenarioContext(
            $"scenario-{context.TestId}-{Guid.NewGuid():N}",
            DateTimeOffset.UtcNow,
            context.TestName);
        context.SetContext(scenario);
        context.RecordObservation("Northstar", "scenario.started", scenario.CorrelationId);
        return Task.CompletedTask;
    }

    public Task AfterTestAsync(ProtoExecutionContext context)
    {
        var scenario = context.Resolve<NorthstarScenarioContext>();
        var duration = DateTimeOffset.UtcNow - scenario.StartedAtUtc;
        context.RecordObservation(
            "Northstar",
            "scenario.completed",
            scenario.CorrelationId,
            new { duration.TotalMilliseconds });
        context.AddAttachment(
            "scenario-summary.json",
            JsonSerializer.Serialize(new { scenario.CorrelationId, scenario.TestName }),
            "application/json");
        return Task.CompletedTask;
    }
}
```

Attachments added in `AfterTestAsync` are still published, because publishing happens after all hooks have finished. The full hook, with its probe milestones and trace events, is `NorthstarScenarioHook` in `samples/Northstar.ProtoTest/NorthstarScenario.cs`.

## What the trace shows

- One `hook.before` operation per test hook and one `hook.after` per hook that completed, each carrying the hook type and its `Order`. The test's `test.setup` operation also records the hook count.
- A hook that fails during setup stops the sequence and appears in the rollback: the hooks that completed run their `AfterTestAsync` in reverse, and the failing hook does not.
- A hook that fails during teardown is recorded as an `Error` finding and does not replace the test's outcome.
- Run hooks are not tied to a test record. Their effects show up in the run entities around the tests, such as a capability or infrastructure state they registered.

## Limits

- Test hooks are registered as singletons and resolved from the root container. Per-test state must come from `context.Services` or the context itself.
- Test hooks receive no token parameter: they read `context.CancellationToken`, which carries the caller's token or the runner's own where its adapter has one (NUnit's test context, the xUnit v2 runner, xUnit v3's `TestContext.Current.CancellationToken`, TUnit's `TestContext.CancellationToken`). MSTest's 4.0.2 floor exposes no token, so those hooks see `CancellationToken.None`.
- `AddTestHook` and `AddRunHook` do **not** dedupe: every call adds another registration. Register each hook once.
- Run hooks get no context, since there is no test yet. See [Lifecycle](./lifecycle.md#the-run) for the run sequence and the rollback rule a `BeforeRunAsync` failure follows.
- A teardown failure in a test hook is recorded as an `Error` finding and does not replace the test's outcome.
