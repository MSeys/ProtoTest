---
sidebar_position: 3
title: Execution context
description: "ProtoExecutionContext lives for exactly one test and holds its clients, state, services, resources, attachments and observations."
---

# Execution context

## What it is

A `ProtoExecutionContext` lives for exactly one test. It is where that test's clients, state, services, resources, attachments and observations are kept.

## Reaching it

```csharp
var context = Proto.Context;
```

`Proto.Context` works anywhere on the test's async flow: in the test method, in helpers it awaits, in page objects, in authenticators. Hooks and attributes receive the context as a parameter instead.

Outside a test, `Proto.Context` throws. The message names the alternatives: off-flow telemetry uses `ProtoHost.FindTraceWriter(Activity?)`, and run-level code uses `ProtoHost.CurrentHost` or the host reference a hook receives. The usual cause is work started with `Task.Run` or a timer callback that escaped the test's flow, or code running in a static initializer.

:::tip[Parallel tests are isolated]
The context is stored in an `AsyncLocal`, so parallel tests each see their own. You do not need to pass it around, and one test cannot accidentally read another's state.
:::

### Test identity

| Member | Meaning |
| --- | --- |
| `TestName` | the name the runner reported |
| `TestMethod` | the `MethodInfo` of the test |
| `Id` | a `ProtoTestId`, see [test ids](./lifecycle.md#test-ids) |
| `TestId` | the id as a zero-padded string |
| `TestNumber` | the id as a `long` |

### Cancellation

`CancellationToken` is the token the caller supplied when the test was started. The runner adapters pass the token their runner exposes:

| Runner | Token the adapter passes | Notes |
| --- | --- | --- |
| NUnit | the test execution context token | cancellable with `[CancelAfter]` |
| xUnit v2 | the runner's `CancellationTokenSource` | |
| xUnit v3 | `TestContext.Current.CancellationToken` | |
| TUnit | `TestContext.CancellationToken` | |
| MSTest | `CancellationToken.None` | `ExecuteAsync(ITestMethod)` exposes no token; setup runs to the integration's own timeout |

```csharp
await using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var context = await host.StartTestAsync("Checkout", method, cancellation.Token);
context.CancellationToken;   // the same token in hooks, attributes and setup I/O
```

`ProtoTest.Sql` passes the token to the connection open and transaction begin. The run-scoped [`IProtoRunHook`](./hooks.md#run-hooks) keeps taking its token as a parameter.

### Unique names

A record that outlives the test process, such as a tenant, an operator or a customer, needs a name that is unique per test and stable across reruns. `UniqueName` derives one from the test id:

```csharp
var tenant = Proto.Context.UniqueName("tenant");        // "tenant-0042317"
var second = Proto.Context.UniqueName("member", 2);     // "member-0042317-2"
```

```csharp
string UniqueName(string name, int sequence = 0);
```

The name is `{name}-{TestId}`, or `{name}-{TestId}-{sequence}` when the optional sequence is given for a second object of the same kind. Because the id carries the run prefix, parallel tests never collide and a rerun against a persistent database or a configured environment never collides either. The same record is reused only when the suite fixes `RunPrefix` (`ConfigureTestIds`), since the default prefix is random per run. `ProtoTest.Data`'s generated member defaults are deterministic per test in the same spirit.

### Typed state

Typed state is how attributes, hooks and tests hand information to each other without globals.

```csharp
public sealed record SampleUserContext(
    string Id, string Tenant, string Email, string Role, string AccessToken) : IProtoContext;

context.SetContext(new SampleUserContext(user.Id, user.Tenant, user.Email, user.Role, user.AccessToken));

var user = Proto.Context.Resolve<SampleUserContext>();        // throws if missing
var maybe = Proto.Context.TryResolve<SampleUserContext>();    // null if missing
```

```csharp
void SetContext<T>(T context) where T : class, IProtoContext;
T Resolve<T>() where T : class, IProtoContext;
T? TryResolve<T>() where T : class, IProtoContext;
```

- `IProtoContext` is an empty marker interface.
- State is keyed by the **exact type** you pass. Setting the same type again replaces it, and you must read it back with the same type, not a base class or interface.
- A missing resolve throws *"No context of type 'X' is registered for this test. Register it before the test body reads it ..."*. Use `TryResolve` in teardown code, where setup may not have got that far.
- `SetContext` records the value as traced state for the context entity. `TryResolve` never traces, and `Resolve` writes a `context.resolve` event only when it fails, so the trace tells you which lookups were missing, not every read.

### Services

The context has its own DI scope, created at test start and disposed at test end.

```csharp
var clock = Proto.Context.Service<IClock>();          // throws if not registered
var mailer = Proto.Context.TryService<IMailer>();     // null if not registered

IServiceProvider services = Proto.Context.Services;
IConfiguration configuration = Proto.Context.Configuration;
```

Register services with `builder.ConfigureServices(...)`. Scoped services are per test.

### Clients

Integrations with a system to talk to register their clients on the context. You normally use their extension methods (`Rest()`, `GraphQL()`, `Web()`). The underlying API:

```csharp
void RegisterClient<TClient>(TClient client, string name = "Default",
    ProtoClientOwnership ownership = ProtoClientOwnership.Context) where TClient : class;
TClient Client<TClient>(string name = "Default") where TClient : class;
TClient? TryClient<TClient>(string name = "Default") where TClient : class;
```

Clients are keyed by type and **case-insensitive** name. Registering the same type and name twice throws, and registering anything after release has begun throws `ObjectDisposedException`. Disposable clients are disposed when the test ends, in reverse registration order. A failing `Client<T>` lookup writes a `client.resolve` event before it throws; `TryClient` never traces. See [Clients](./clients.md) for writing your own.

### Resources

A test can own resources, such as temporary files, provisioned data or an enlistment, and have them released in reverse registration order during teardown, before the DI scope is disposed.

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
- Each resource is released at most once, in reverse registration order, after all hooks and attributes have run. `ReleaseResourceAsync` releases one early and returns `false` for an unknown or already-released id.
- Framework-managed clients live on the client entity and appear in the report only if their release failed.

```csharp
context.RegisterResource(
    "mailbox:cleanup",
    "mailbox",
    "Delete the test's mailbox",
    release => new ValueTask(mailbox.DeleteAsync(release.Test!.TestId, release.CancellationToken)));
```

### Findings

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

### Attachments

```csharp
ProtoTestAttachment AddAttachment(string name, string content, string mediaType = "text/plain", string? description = null);
ProtoTestAttachment AddAttachment(string name, ReadOnlyMemory<byte> content, string mediaType = "application/octet-stream", string? description = null);
ProtoTestAttachment AddAttachmentFile(string filePath, string? name = null, string mediaType = "application/octet-stream", string? description = null);
ProtoTestAttachment AddAttachment(ProtoTestAttachment attachment);

IReadOnlyList<ProtoTestAttachment> Attachments { get; }
```

Names without a prefix are stored as `{testId}-{name}`, and a duplicate name (case-insensitive) throws. See [Attachments](./attachments.md).

### Observations

```csharp
void RecordObservation(string targetName, string kind, string identifier,
    object? data = null, IReadOnlyDictionary<string, object>? metadata = null);
void RecordObservation(ProtoObservation observation);

IReadOnlyCollection<ProtoObservation> RecordedObservations { get; }
```

Recording stores the observation, dispatches it to every collector that accepts it, and traces it. See [Coverage and observations](../observability/coverage.md).

### Trace

```csharp
IProtoTraceWriter Trace { get; }
```

Add your own entries to the test's trace. See [Extending ProtoTest](../advanced/extending.md#adding-to-the-trace).

## Limits

- `RegisterResource` refuses run-scoped resources. `AddResource` on the builder is the run-scoped counterpart.
- `context.DisposeAsync` is idempotent and attempts every release. Failures are aggregated.
- A skipped test never creates a context. See [Skip conditions](./skip-conditions.md).
- `Proto.Context` cannot answer on flows that escaped the test. See [Concurrency](./concurrency.md) for the isolation rule both pages share.
