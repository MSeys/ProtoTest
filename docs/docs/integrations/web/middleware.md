---
sidebar_position: 7
title: Waits and middleware
---

# Waits and middleware

Real applications have their own notion of "ready": a loading spinner, an in-flight XHR, an animation. Rather than sprinkling `Task.Delay` through tests, register it once.

## Wait conditions

A wait condition is polled around the operations you choose until it reports ready.

```csharp
public interface IWebWaitCondition
{
    string Name { get; }
    ValueTask<WebWaitObservation> ObserveAsync(WebWaitContext context, CancellationToken cancellationToken = default);
}
```

```csharp
public sealed class NoSpinnerWait : IWebWaitCondition
{
    public string Name => "no loading spinner";

    public async ValueTask<WebWaitObservation> ObserveAsync(
        WebWaitContext context,
        CancellationToken cancellationToken = default)
    {
        var busy = await context.EvaluateBooleanAsync(
            "document.querySelector('[aria-busy=true]') !== null",
            cancellationToken);

        return busy
            ? WebWaitObservation.Pending("a region is still aria-busy")
            : WebWaitObservation.Ready();
    }
}
```

Register it with the timing and the operations it applies to:

```csharp
builder.AddWebWait<NoSpinnerWait>(
    WebWaitTiming.After,
    timeout: TimeSpan.FromSeconds(10),
    pollInterval: TimeSpan.FromMilliseconds(100),
    WebOperationKind.Navigate, WebOperationKind.Click);
```

```csharp
public static IProtoHostBuilder AddWebWait<TCondition>(
    this IProtoHostBuilder builder,
    WebWaitTiming timing,                // Before or After the operation
    TimeSpan? timeout = null,            // default 5 s
    TimeSpan? pollInterval = null,       // default 50 ms
    params WebOperationKind[] operations)
    where TCondition : class, IWebWaitCondition;
```

With no operations listed, the wait applies to `Navigate`, `Click` and `Fill`. The full list of operation kinds is `Navigate`, `Click`, `Fill`, `Check`, `SelectOption`, `Press`, `Count`, `ReadText`, `ReadValue`, `IsVisible`, `IsEnabled`, `IsChecked` and `Assert`.

If the condition isn't ready in time, the operation fails with `WebWaitTimeoutException`, including the last observation you returned — so make those messages useful.

### What a condition can check

`WebWaitContext` gives you:

| Member | |
| --- | --- |
| `Operation` | the `WebOperationContext` being waited around |
| `EvaluateBooleanAsync(script)` | evaluate JavaScript in the page |
| `IsVisibleAsync(element)` | check an element's visibility |
| `CountAsync(elements)` | count matches |

The latter two take a `WebElementReference` — get one from any element's `Reference` property.

### Built in: jQuery

`JQueryIdleWait` waits until `jQuery.active === 0`, and is a no-op on pages without jQuery:

```csharp
builder.AddWebWait<JQueryIdleWait>(WebWaitTiming.After);
```

Each wait shows up in the trace as a `web.wait` entry with its condition, timeout and last observation.

## Middleware

Middleware wraps every web operation — for logging, timing, retries or extra diagnostics.

```csharp
public interface IWebOperationMiddleware
{
    ValueTask InvokeAsync(
        WebOperationContext context,
        WebOperationDelegate next,
        CancellationToken cancellationToken = default);
}
```

```csharp
public sealed class SlowOperationWarning(ILogger<SlowOperationWarning> logger) : IWebOperationMiddleware
{
    public async ValueTask InvokeAsync(
        WebOperationContext context,
        WebOperationDelegate next,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        await next(context, cancellationToken);

        var elapsed = Stopwatch.GetElapsedTime(started);
        if (elapsed > TimeSpan.FromSeconds(2))
            logger.LogWarning("{Operation} on {Element} took {Elapsed}", context.Kind, context.Name, elapsed);
    }
}
```

```csharp
builder.AddWebMiddleware<SlowOperationWarning>();
```

Middleware and wait conditions are resolved from dependency injection, so their constructors can take any registered service.

`WebOperationContext` exposes `Execution` (the test's `ProtoExecutionContext`), `Kind`, `Name`, `BackendName`, `SessionName`, `CorrelationId`, `Element` and — after `next` returns — `Result`.

Middleware nests like ASP.NET Core's: the **first one registered is the outermost**, and the backend call sits in the middle. All wait conditions run inside one built-in middleware, which takes its place in that order at your first `AddWebWait` call.

Middleware is created once per test (scoped), and registering the same middleware type twice has no extra effect.
