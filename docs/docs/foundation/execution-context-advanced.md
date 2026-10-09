---
sidebar_position: 4
title: Execution context advanced topics
sidebar_label: Advanced topics
description: "The execution context pieces a suite reaches for less often: services, resources, findings, observations and the trace writer."
---

# Execution context advanced topics

The compartments below build on [Execution context](./execution-context.md). Most tests use clients and typed state. These five cover services, ownership, reporting and the trace.

## Services

The context has its own DI scope, created at test start and disposed at test end.

```csharp
var clock = Proto.Context.Service<IClock>();          // throws if not registered
var mailer = Proto.Context.TryService<IMailer>();     // null if not registered

IServiceProvider services = Proto.Context.Services;
IConfiguration configuration = Proto.Context.Configuration;
```

Register services with `builder.ConfigureServices(...)`. Scoped services are per test.

## Resources

A test can own resources, such as temporary files, provisioned data or an enlistment. They are released in reverse registration order during teardown, before the DI scope is disposed.

```csharp
void RegisterResource(IProtoResource resource);
T RegisterResource<T>(T resource) where T : class, IProtoResource;
void RegisterResource(string id, string kind, string description,
    Func<ProtoResourceReleaseContext, ValueTask> release);
ValueTask<bool> ReleaseResourceAsync(string id);

IReadOnlyList<ProtoResourceSnapshot> Resources { get; }
```

- Registration is **test-scoped only.** A resource whose `Scope` is not `Test` throws: *"Register it with AddResource on the host builder instead."*
- A duplicate id throws, and registration after release has begun throws `ObjectDisposedException`.
- Each resource is released at most once, in reverse registration order, after all hooks and attributes have run. `ReleaseResourceAsync` releases one early and returns `false` when the id is unknown or a release for it has already started. When the release this call runs fails, the resource is recorded as `ReleaseFailed` and the original exception is thrown. Dispose does not retry that release. The failure still counts in the test's cleanup outcome, the same way a release that fails during dispose does: a teardown finding, without a separate rule for whether the test or the run is red.
- Dispose waits for a release that is already running, with no timeout, before it disposes the service scope. A release callback must not dispose its own context, and must not call `ReleaseResourceAsync` for the resource it is releasing. Both throw `InvalidOperationException` instead of waiting.
- Framework-managed clients live on the client entity and appear in the report only if their release failed.

```csharp
context.RegisterResource(
    "mailbox:cleanup",
    "mailbox",
    "Delete the test's mailbox",
    release => new ValueTask(mailbox.DeleteAsync(release.Test!.TestId, release.CancellationToken)));
```

## Findings

A finding is something worth reporting that is deliberately **not** a failure: a slow response, a deprecated field, a teardown problem. Findings reach the run's reports and [run gates](./lifecycle.md#run-gates-and-resources), and are traced so they appear in ProtoTrace.

```csharp
ProtoReportItem AddFinding(
    string message,
    ProtoReportStatus status = ProtoReportStatus.Warning,
    string? identifier = null,
    string? category = null,
    string? targetName = null,
    IReadOnlyList<string>? tags = null,
    IReadOnlyDictionary<string, object>? metadata = null);
```

Defaults: status `Warning`, identifier `finding-NNN` per context, category `Finding`, target `Test findings`, display group the test name. Metadata is merged with `test.id` and `test.name`. A run gate can fail the run when an `Error` finding exists.

## Observations

```csharp
void RecordObservation(string targetName, string kind, string identifier,
    object? data = null, IReadOnlyDictionary<string, object>? metadata = null);
void RecordObservation(ProtoObservation observation);

IReadOnlyCollection<ProtoObservation> RecordedObservations { get; }
```

Recording stores the observation, dispatches it to every collector that accepts it, and traces it. See [Coverage and observations](../observability/coverage.md).

## Trace

```csharp
IProtoTraceWriter Trace { get; }
```

Add your own entries to the test's trace. See [Extending ProtoTest](../advanced/extending.md#adding-to-the-trace).
