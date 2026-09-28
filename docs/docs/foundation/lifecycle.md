---
sidebar_position: 3
title: Host and lifecycle
description: "How the ProtoTest host is built, started and stopped, and the order in which a test's hooks, attributes and clients run."
---

# Host and lifecycle

## What it is

`ProtoHost` is built once per test process by your runner's [assembly setup](../runners/overview.md). It owns the dependency injection container, runs suite-wide hooks and [run gates](#run-gates-and-resources), starts [infrastructure](./infrastructure.md), and starts and completes each test.

`ProtoExecutionContext` exists for exactly one test. It holds that test's clients, typed state, resources, attachments and observations. See [Execution context](./execution-context.md).

## How it works

### Building the host

The runner's setup class receives an `IProtoHostBuilder`:

```csharp
protected override void Configure(IProtoHostBuilder builder) =>
    builder
        .ConfigureAppConfiguration(configuration => configuration.AddJsonFile("appsettings.Test.json", optional: true))
        .ConfigureServices(services => services.AddSingleton<IClock, FixedClock>())
        .ConfigureTracing(trace => trace.OutputPath = "TestResults/run.prototrace")
        .ConfigureTestIds(ids => ids.RunPrefix = 42)
        .AddRunHook<StartDependenciesHook>()
        .AddTestHook<ResetMailboxHook>()
        .AddApplication("Api", app => app.AddRest(rest => rest.AddClient("Api")));
```

```csharp
IProtoHostBuilder ConfigureServices(Action<IServiceCollection> configure);
IProtoHostBuilder ConfigureAppConfiguration(Action<IConfigurationBuilder> configure);
IProtoHostBuilder ConfigureTracing(Action<ProtoTraceOptions> configure);
IProtoHostBuilder ConfigureTestIds(Action<ProtoTestIdOptions> configure);
IProtoHostBuilder AddTestHook<THook>() where THook : class, IProtoTestHook;
IProtoHostBuilder AddRunHook<TRunHook>() where TRunHook : class, IProtoRunHook;
IProtoHostBuilder AddRunGate<TGate>() where TGate : class, IProtoRunGate;
IProtoHostBuilder AddRunGate(string name, Func<ProtoRunGateContext, ProtoRunGateResult> evaluate);
IProtoHostBuilder AddResource(IProtoResource resource);
ProtoHost Build();
```

Every integration's `Add...` method is an extension on this same builder. A builder can only build once. The option tables for `ProtoTestIdOptions` and `ProtoTraceOptions` are in [Configuration](../getting-started/configuration.md#host-options-code-only).

### The run

```mermaid
sequenceDiagram
    participant Runner
    participant Host as ProtoHost
    participant RunHooks as Run hooks
    Runner->>Host: StartAsync
    Host->>RunHooks: BeforeRunAsync (ascending Order)
    Note over Runner,Host: tests run
    Runner->>Host: StopAsync
    Host->>RunHooks: AfterRunAsync (descending Order)
    Note over Host: gates evaluate, report sinks export, run resources release, then the .prototrace is written
```

- If a `BeforeRunAsync` throws, the hooks that already started get their `AfterRunAsync` in reverse, and startup fails.
- `StopAsync` runs every `AfterRunAsync` even if some throw, then reports all failures together.
- Once stopping has begun, starting a new test throws.

ProtoTest's own run hooks are ordered to run **last** on the way out: run gates evaluate first, report sinks export next, run-scoped resources release, and the trace archive is written last, so it can include the reports.

### A test

When the runner starts a test:

1. A `ProtoExecutionContext` and a new DI scope are created, and the context becomes `Proto.Context` for this async flow.
2. **Every `IProtoTestHook`** runs `BeforeTestAsync`, in ascending `Order`. ProtoTest's client-initializing hook has the lowest possible order, so clients exist before any of your hooks run.
3. **Every `ProtoAttribute`** on the test runs `BeforeTestAsync`, in ascending `Order`.
4. Your test body runs.

**All hooks run before any attribute**, whatever their `Order` values. Among attributes, only `Order` matters: whether an attribute sits on the class or the method does not change when it runs, and ties keep class attributes first.

When the runner completes the test:

1. Attributes run `AfterTestAsync` in **reverse** order.
2. Hooks run `AfterTestAsync` in **reverse** order.
3. [Attachments](./attachments.md) are published to the runner.
4. `context.DisposeAsync` releases owned resources in reverse registration order, then disposes the DI scope.
5. The test's trace artifacts are captured and the recorder is completed with its outcome. `Proto.Context` is cleared.

**Every step is attempted**, even when an earlier one throws. A teardown failure is recorded as an `Error` finding and does not replace the outcome the test already reported, so a cleanup error never hides a failed assertion. The failure still surfaces to the runner: one exception is rethrown as-is, several become one `AggregateException`.

### When setup fails

If a hook or attribute throws during setup, ProtoTest **rolls back**: only the components that *completed* their `BeforeTestAsync` get their `AfterTestAsync`, in reverse. The one that threw does not.

```
FirstHook:Before
SecondHook:Before
FirstAttribute:Before
FailingAttribute:Before   <- throws
FirstAttribute:After
SecondHook:After
FirstHook:After
```

The test is recorded as failed, the trace shows a `Rollback` phase instead of `Teardown`, and the exception reads *"Test setup failed and completed lifecycle components were rolled back."*

This is why teardown code should tolerate partial setup. The sample environment attribute uses `TryResolve` rather than `Resolve` in its `AfterTestAsync` for exactly that reason.

### One test per async flow

A context is tied to the async flow that started it. Starting a second test on the same flow before completing the first throws, as does completing a test from a different host. Skipped tests never reach this point: a [skip condition](./skip-conditions.md) is evaluated before `StartTestAsync`, so there is no context to complete.

## How to use it

Runner packages call `StartTestAsync` and `CompleteTestAsync` for you. You would only call them yourself when building a runner integration, or when testing ProtoTest extensions, as the repository's own tests do:

```csharp
var host = new ProtoHostBuilder().AddRest().Build();
await using var ownedHost = host;
await host.StartAsync();

var context = await host.StartTestAsync("my test", testMethod);
// ...
await host.CompleteTestAsync(ProtoTestResult.Passed);
```

`ProtoTestResult` has `Passed`, `Skipped`, `Unknown`, `Failed(exception)`, `Failed(error)` and `Cancelled(exception)`.

The host itself:

```csharp
public sealed class ProtoHost : IAsyncDisposable
{
    static ProtoExecutionContext CurrentContext { get; }
    static ProtoExecutionContext? CurrentContextOrNull { get; }
    static ProtoHost CurrentHost { get; }
    static IProtoTraceWriter? FindTraceWriter(ActivityTraceId traceId);
    static IProtoTraceWriter? FindTraceWriter(Activity? activity);

    IConfiguration Configuration { get; }
    IProtoTraceSource Trace { get; }
    bool HasCapability(string kind, string? name = null);

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);

    Task<ProtoExecutionContext> StartTestAsync(string testName, MethodInfo testMethod,
        IEnumerable<ProtoAttribute>? attributes = null, IProtoTestAttachmentPublisher? attachmentPublisher = null);
    Task<ProtoExecutionContext> StartTestAsync(string testName, string testId, MethodInfo testMethod,
        IEnumerable<ProtoAttribute>? attributes = null, IProtoTestAttachmentPublisher? attachmentPublisher = null);
    Task<ProtoExecutionContext> StartTestAsync(string testName, MethodInfo testMethod,
        CancellationToken cancellationToken);
    Task<ProtoExecutionContext> StartTestAsync(string testName, string testId, MethodInfo testMethod,
        CancellationToken cancellationToken);

    Task CompleteTestAsync();                          // outcome Unknown
    Task CompleteTestAsync(ProtoTestResult result);
}
```

`Proto.Host` returns the host of the current test, or, outside a test, the only active host (with more than one active, it throws).

### Test ids

Every test gets a numeric id, and everything the test produces is keyed by it: trace entries, archived artifacts, and often the data your own attributes create (`$"test-{context.TestId}"`).

An id is a **run prefix** followed by a **sequence**: with the defaults, `482913000001`, `482913000002`, and so on.

| `ProtoTestIdOptions` | Default |
| --- | --- |
| `RunPrefix` | a random six-digit number per host |
| `SequenceDigits` | `6` (1 to 9) |

The random prefix keeps ids from colliding when several test processes create data in the same shared environment. Set a fixed `RunPrefix`, for example from a CI build number, when you want ids you can trace back to a pipeline run. Ids are at most 18 digits; running out of sequence numbers throws.

To replace the scheme entirely, register your own `IProtoTestIdGenerator`:

```csharp
public interface IProtoTestIdGenerator
{
    ProtoTestId Next(MethodInfo testMethod);
}
```

### Run gates and resources

`AddRunGate` registers a check that runs **once, after the last test and before the reports are written**, so it can see everything the run's collectors produced. `ProtoRunGateContext` exposes `Items`, `ItemsOfKind`, `InCategory`, `ForTarget` and `WithStatus`, plus coverage helpers such as `CoverageFor(target)`. A result is `Passed`, `Warning`, `Failed` or `Skipped`; a gate that returns no result is treated as failed, and a failed gate throws `ProtoRunGateException` out of `AfterRunAsync`. The delegate overload is the quick form:

```csharp
builder.AddRunGate("no error findings", context => context
    .ItemsOfKind(ProtoReportItemKinds.Finding)
    .Any(item => item.Status == ProtoReportStatus.Error)
    ? ProtoRunGateResult.Failed("The run recorded error findings.")
    : ProtoRunGateResult.Passed("No error findings were recorded."));
```

`AddResource(IProtoResource)` registers an already-created, run-scoped resource, such as a started container or a connection, that the host owns and releases with the run, without starting anything. `AddInfrastructure` is the variant that starts with the run and fills settings; see [Infrastructure](./infrastructure.md).

## What the trace shows

- Each test's record opens with a `test.setup` operation, carries one `hook.before` and `hook.after` per hook (with its type and order) and one `attribute.before` and `attribute.after` per attribute, then the test's own operations.
- A failed setup shows a `Rollback` phase instead of `Teardown`, and only the components that completed appear.
- Attachments land on the record of the operation that produced them. Resources appear as entities, and a failed release writes a `resource.release` operation.
- Run-level pieces are entities: capabilities, infrastructure with its state, and readiness probes. Run gates evaluate report items after the last test and before the reports export.
- The trace archive is written last, after the reports, so a report's coverage is inside the archive.
- A teardown failure is recorded as an `Error` finding without changing the test's outcome.

## Limits

- **One context per async flow.** Starting a second test on the same flow before completing the first throws, and completing a test from a different host throws.
- **One build per builder.** `Build()` can only run once, and once stopping has begun, starting a new test throws.
- **A skipped test never reaches the lifecycle.** It has no context, no trace record and no teardown.
- **A teardown failure does not replace the outcome.** It is recorded as an `Error` finding and still surfaces to the runner as an exception.
- **Ids cannot grow past 18 digits.** Running out of sequence numbers throws. `Proto.Context` is flow-local: work that escapes the test's flow cannot read it. See [Execution context](./execution-context.md) and [Concurrency](./concurrency.md).
