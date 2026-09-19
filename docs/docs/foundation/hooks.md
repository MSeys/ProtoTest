---
sidebar_position: 7
title: Hooks
description: "Run code around every test or around the whole run without touching a test: correlation ids, shared resets, one-off startup."
---

# Hooks

Hooks run code around every test, or around the whole run, without touching any test. Use them for cross-cutting behaviour — correlation ids, resetting a shared mailbox, starting a container once.

## Test hooks

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

```csharp
builder.AddTestHook<ResetMailboxHook>();
```

Test hooks are registered as **singletons**, and constructor parameters are resolved from the root container. For per-test services, resolve them from `context.Services` inside the method.

### A fuller example

The sample suite's `NorthstarScenarioHook` gives every test a correlation id, records observations, and attaches a summary:

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
}
```

Its `AfterTestAsync` resolves the state, records a `scenario.completed` observation and adds a `scenario-summary.json` attachment. Attachments added in `AfterTestAsync` are still published — publishing happens after all hooks have finished. The full hook is [`samples/Northstar.ProtoTest/NorthstarScenario.cs`](../../../samples/Northstar.ProtoTest/NorthstarScenario.cs).

## Run hooks

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

```csharp
builder.AddRunHook<StartDependenciesHook>();
```

Run hooks run once, before the first test and after the last. They don't receive a context — there's no test yet.

## Ordering

| | Before | After |
| --- | --- | --- |
| Test hooks | ascending `Order` | descending |
| Run hooks | ascending `Order` | descending |

Lower runs earlier on the way in and later on the way out, so a hook with `Order = -1_000` wraps everything with a higher order.

ProtoTest's built-in hooks sit at the extremes on purpose:

| Hook | `Order` | Why |
| --- | --- | --- |
| Client initializer (test) | `int.MinValue` | runs first on the way in, so your hooks can use clients |
| Trace export (run) | `int.MinValue` | runs last on the way out, after reports and resources |
| Run resources (run) | `int.MinValue + 1` | releases run-scoped resources before the trace archive is written |
| Report sinks (run) | `int.MinValue + 2` | exports reports before resources are released, so the report is a snapshot of the run |
| Run gates (run) | `int.MinValue + 3` | evaluates first on the way out, before reports export |
| HTTP auth (test) | `100` | applies `[Auth<T>]` after your hooks, so it can override the request |

The hook that creates clients runs **first** on the way in, so your hooks can use them. The run hooks that export reports, release resources and write the trace archive run **last** on the way out, in the reverse order above.

Integrations add their own test hooks too — the HTTP integrations apply `[Auth<T>]` from a hook with `Order = 100`.

Remember that **all test hooks run before any [attribute](./attributes.md)**. See [Host and lifecycle](./lifecycle.md) for the full sequence and failure rules.

## Limits

- Test hooks are registered as singletons and resolved from the root container; per-test state must come from `context.Services` or the context itself.
- `AddTestHook` and `AddRunHook` do **not** dedupe: every call adds another registration. Register each hook once.
- Run hooks get no context — there is no test yet — and `BeforeRunAsync` failures roll back only the hooks that already started, in reverse.
- A teardown failure in a test hook is recorded as an `Error` finding and does not replace the test's outcome, but it still surfaces to the runner.

## Hook or attribute?

| Use a hook when… | Use an attribute when… |
| --- | --- |
| it applies to every test | it applies to *some* tests |
| tests shouldn't have to know about it | it's part of what the test is describing |
| e.g. correlation ids, cleanup of shared state | e.g. "a fresh tenant", "as a billing admin" |
