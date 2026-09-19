---
sidebar_position: 2
title: Execution context
description: "ProtoExecutionContext lives for exactly one test and holds its clients, state, services, attachments and observations."
---

# Execution context

A `ProtoExecutionContext` lives for exactly one test. It's where that test's clients, state, services, attachments and observations are kept.

## Reaching it

```csharp
var context = Proto.Context;
```

`Proto.Context` works anywhere on the test's async flow — in the test method, in helpers it awaits, in page objects, in authenticators. Hooks and attributes receive the context as a parameter instead.

Outside a test, `Proto.Context` throws *"No active ProtoExecutionContext available on this thread."* The usual cause is work started with `Task.Run` or a timer callback that escaped the test's flow, or using it from a static initializer.

:::tip[Parallel tests are isolated]
The context is stored in an `AsyncLocal`, so parallel tests each see their own. You don't need to pass it around, and one test can't accidentally read another's state.
:::

## Test identity

| Member | |
| --- | --- |
| `TestName` | the name the runner reported |
| `TestMethod` | the `MethodInfo` of the test |
| `Id` | a `ProtoTestId` — see [test ids](./lifecycle.md#test-ids) |
| `TestId` | the id as a zero-padded string |
| `TestNumber` | the id as a `long` |

## Typed state

Typed state is how attributes, hooks and tests hand information to each other without globals.

```csharp
public sealed record SampleUserContext(
    string Id, string Tenant, string Email, string Role, string AccessToken) : IProtoContext;
```

```csharp
context.SetContext(new SampleUserContext(user.Id, user.Tenant, user.Email, user.Role, user.AccessToken));
```

```csharp
var user = Proto.Context.Resolve<SampleUserContext>();        // throws if missing
var maybe = Proto.Context.TryResolve<SampleUserContext>();    // null if missing
```

```csharp
void SetContext<T>(T context) where T : class, IProtoContext;
T Resolve<T>() where T : class, IProtoContext;
T? TryResolve<T>() where T : class, IProtoContext;
```

- `IProtoContext` is an empty marker interface.
- State is keyed by the **exact type** you pass. Setting the same type again replaces it, and you must read it back with the same type — not a base class or interface.
- A missing `Resolve<T>()` throws *"No context of type 'X' registered."* Use `TryResolve` in teardown code, where setup may not have got that far.
- Every set and read is traced — including a snapshot of the value — so the [trace viewer](../observability/prototrace.md) shows what state each step saw.

## Services

The context has its own DI scope, created at test start and disposed at test end.

```csharp
var clock = Proto.Context.Service<IClock>();          // throws if not registered
var mailer = Proto.Context.TryService<IMailer>();     // null if not registered

IServiceProvider services = Proto.Context.Services;
IConfiguration configuration = Proto.Context.Configuration;
```

Register services with `builder.ConfigureServices(...)`. Scoped services are per test.

## Clients

Integrations register their clients on the context; you normally use their extension methods (`Rest()`, `GraphQL()`, `Web()`). The underlying API:

```csharp
void RegisterClient<TClient>(TClient client, string name = "Default", bool disposeWithContext = true) where TClient : class;
TClient Client<TClient>(string name = "Default") where TClient : class;
TClient? TryClient<TClient>(string name = "Default") where TClient : class;
```

Clients are keyed by type and **case-insensitive** name. Registering the same type and name twice throws. Disposable clients are disposed when the test ends, in reverse registration order. See [Clients](./clients.md) for writing your own.

## Attachments

```csharp
ProtoTestAttachment AddAttachment(string name, string content, string mediaType = "text/plain", string? description = null);
ProtoTestAttachment AddAttachment(string name, ReadOnlyMemory<byte> content, string mediaType = "application/octet-stream", string? description = null);
ProtoTestAttachment AddAttachmentFile(string filePath, string? name = null, string mediaType = "application/octet-stream", string? description = null);
ProtoTestAttachment AddAttachment(ProtoTestAttachment attachment);

IReadOnlyList<ProtoTestAttachment> Attachments { get; }
```

See [Attachments](./attachments.md).

## Observations

```csharp
void RecordObservation(string targetName, string kind, string identifier,
    object? data = null, IReadOnlyDictionary<string, object>? metadata = null);
void RecordObservation(ProtoObservation observation);

IReadOnlyCollection<ProtoObservation> RecordedObservations { get; }
```

See [Coverage and observations](../observability/coverage.md).

## Trace

```csharp
IProtoTraceWriter Trace { get; }
```

Add your own entries to the test's trace — see [Extending ProtoTest](../advanced/extending.md#adding-to-the-trace).
