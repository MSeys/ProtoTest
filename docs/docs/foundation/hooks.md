---
sidebar_position: 4
title: Hooks
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

The sample suite uses a hook to give every test a correlation id, record observations, and attach a summary:

```csharp
public sealed class SaasScenarioHook : IProtoTestHook
{
    public int Order => -1_000;

    public Task BeforeTestAsync(ProtoExecutionContext context)
    {
        var correlation = new ScenarioCorrelationContext(
            $"scenario-{context.TestId}-{Guid.NewGuid():N}",
            DateTimeOffset.UtcNow,
            context.TestName);
        context.SetContext(correlation);

        context.Trace.WriteEvent(
            "saas.correlation.begin",
            "Begin correlated SaaS scenario",
            "ProtoTest.SampleApp.Testing",
            outcome: ProtoTraceOutcome.Succeeded,
            attributes: new Dictionary<string, string?>
            {
                ["saas.correlation_id"] = correlation.CorrelationId
            });

        return Task.CompletedTask;
    }

    public Task AfterTestAsync(ProtoExecutionContext context)
    {
        var correlation = context.Resolve<ScenarioCorrelationContext>();
        var duration = DateTimeOffset.UtcNow - correlation.StartedAtUtc;

        context.AddAttachment(
            "scenario-summary.json",
            JsonSerializer.Serialize(new { correlation.CorrelationId, DurationMs = duration.TotalMilliseconds }),
            "application/json",
            "Correlation for this scenario.");

        return Task.CompletedTask;
    }
}
```

Attachments added in `AfterTestAsync` are still published — publishing happens after all hooks have finished.

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

- The hook that creates clients runs **first** on the way in, so your hooks can use them.
- The hooks that export reports and write the trace archive run **last** on the way out.

Integrations add their own test hooks too — the HTTP integrations apply `[Auth<T>]` from a hook with `Order = 100`.

Remember that **all test hooks run before any [attribute](./attributes.md)**. See [Host and lifecycle](./lifecycle.md) for the full sequence and failure rules.

## Hook or attribute?

| Use a hook when… | Use an attribute when… |
| --- | --- |
| it applies to every test | it applies to *some* tests |
| tests shouldn't have to know about it | it's part of what the test is describing |
| e.g. correlation ids, cleanup of shared state | e.g. "a fresh tenant", "as a billing admin" |
