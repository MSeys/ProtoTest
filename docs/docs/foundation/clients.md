---
sidebar_position: 6
title: Clients
description: "Clients are what a test talks to. ProtoTest creates them per test, registers them on the context and disposes them afterwards."
---

import TraceAnatomy from '@site/src/components/TraceAnatomy';
import {lessonTraces} from '@site/src/data/traceSources';

export const clientLayers = [
  {
    id: 'initialize',
    label: 'Initialize',
    when: 'setup, before any other hook',
    lead: 'One client.initialize operation per group, in registration order. The in-process client carries the server start.',
    entries: [
      {kind: 'client.initialize', name: 'Rest:Northstar:Northstar (HttpClient)', meta: '3.78 ms, address deferred to the in-process server'},
      {kind: 'client.initialize', name: 'GraphQL:Northstar:GraphQL (HttpClient)', meta: '0.21 ms, same name, other protocol slot'},
      {kind: 'client.initialize', name: 'Rest:Northstar web:Northstar web (HttpClient)', meta: '1.08 ms, address from configuration'},
      {kind: 'client.initialize', name: 'Northstar (HttpClient)', meta: '317.69 ms, carries the ASP.NET Core server start'},
      {kind: 'client.initialize', name: 'ScenarioProbe, Default (ProtoMessageClient)', meta: 'typed and messaging clients'},
    ],
  },
  {
    id: 'release',
    label: 'Release',
    when: 'teardown',
    lead: 'Owned clients release as entity state. The shared factory stays: owned false, no dispose.',
    entries: [
      {kind: 'client', name: 'Northstar, Rest, GraphQL, probe, messaging clients', meta: 'client.owned true, resource.state released'},
      {kind: 'client', name: 'Northstar:Factory', meta: 'client.owned false, shared with the run'},
    ],
  },
];

# Clients

## What it is

A client is anything a test talks to, for example an `HttpClient` or a browser session. ProtoTest creates clients **per test**, registers them on the context, and disposes them afterwards. Most integrations use this mechanism for the systems they connect to, and you can use it for your own.

```text
initializer chain (registration order, first true wins)
  -> RegisterClient -> test uses -> CompleteAsync -> dispose reversed
ownership: Context disposes, Caller shares
```

## How it works

### The context API

```csharp
void RegisterClient<TClient>(TClient client, string name = "Default",
    ProtoClientOwnership ownership = ProtoClientOwnership.Context)
    where TClient : class;
TClient Client<TClient>(string name = "Default") where TClient : class;
TClient? TryClient<TClient>(string name = "Default") where TClient : class;
```

Clients are keyed by **type and case-insensitive name**: an `HttpClient` named `Api` and a web session named `Api` can coexist. Registering the same type and name twice throws, and registering after release has begun throws `ObjectDisposedException`. A failed `Client<T>` lookup records a `client.resolve` event before throwing; `TryClient` never traces.

### When clients are created and released

Before any of your hooks or attributes run, ProtoTest's client hook groups initializers by protocol, client type and name, then tries each group in registration order. The trace records one `client.initialize` operation per group, not one per attempt, and the winning initializer is written as the client entity's `client.initializer` state. A candidate that returns `false` is not traced individually; an initializer that throws fails the `client.initialize` operation.

Integrations such as REST and GraphQL set `Protocol`, so they can each register an `HttpClient` named `Default`. Their clients are stored under names such as `Rest:Default` and `GraphQL:Default`, and the integration accessors resolve those names for you. A client with an unambiguous name can also be looked up by its bare name. If several protocols use that name, use the scoped name with `Client<T>` or use the integration accessor.

| You ask for | Stored under | You reach |
| --- | --- | --- |
| `Rest("Default")` | `Rest:Default` | the REST `HttpClient` |
| `GraphQL("Default")` | `GraphQL:Default` | the GraphQL `HttpClient` |
| `Rest("Northstar")` under `[Application("Northstar")]` | `Rest:Northstar:Northstar` | the application's REST `HttpClient` |

If no initializer in a group succeeds, the test fails in setup with *"No registered initializer could create a client of type 'X' with name 'Y'."*

If a client implements `IDisposable` or `IAsyncDisposable`, it is disposed when the test ends, in reverse order of registration. Client resources are framework-managed: a successful release is recorded as the client entity's `resource.state = released` rather than a `resource.release` operation, and a **failed** release writes a `resource.release` event so the failure is explainable.

A client that implements `IProtoClientCompletion` also gets `CompleteAsync()` after normal teardown hooks, before disposal and report publication. A bare-name alias does not cause completion to run twice.

### Resolving a named client

The integration accessors, `Rest(name)`, `GraphQL(name)`, `Grpc(name)` and `Devices(name)`, resolve a client name in one order:

| Step | Rule |
| --- | --- |
| 1. Qualified with the selected application | `Rest("Api")` under `[Application("Csms")]` looks for `Csms:Api` first, so the ambient application wins |
| 2. The requested name as given | keeps a host- or user-registered client reachable by its own name |
| 3. Unique across the host | when exactly one client of that protocol has that unqualified name, it resolves, whichever application registered it |

```text
name has ':'?  ->  exact, no re-qualification, no unique-name fallback
bare name under one app, one match?  ->  resolves
bare name, two apps, same name?  ->  ambiguous error naming both; qualify or bind
```

A name that is already qualified (`App:Client`, containing `:`) is exact: it is not re-qualified with the selected application and does not fall back to the unique-name lookup.

When two applications register the same client name (`Csms:Api` and `Dashboard:Api`), the bare name is ambiguous: the lookup fails naming both qualified candidates. Qualify the call (`Rest("Csms:Api")`) or bind it for the test with `[Application("Csms", "Rest:Api")]`. The unnamed accessor (`Rest()`) always keeps the selected application's binding, then its first registered client for the protocol, then `"Default"`. A collision never changes that choice.

### Fallback chains

Several initializers can offer the same protocol, client type and name. They are tried **in registration order**, and the first to return `true` wins. Returning `false` means "not me", and the initializer must leave the context untouched when it does. An initializer without `Protocol` can serve as a fallback for matching protocol chains.

This is how a client bound to an application is served by a real URL when the application has an address, configured or published by a started infrastructure piece, and by the [in-process ASP.NET Core server](../integrations/aspnetcore.md) otherwise:

```csharp
public Task<bool> TryInitializeAsync(ProtoExecutionContext context)
{
    var url = ProtoApplication.BaseUrl(context, Name);   // settings first, then configuration
    if (url is null) return Task.FromResult(false);      // let the next initializer try

    context.RegisterClient(new HttpClient { BaseAddress = new Uri(url) }, Name);
    return Task.FromResult(true);
}
```

A client whose address resolves is built over **its own primary handler for that test**, with the named client's configured handlers still in the chain. The handler owns the cookie container, so a sign-in one test performs never reaches a parallel test through a shared handler pool: every test starts with an empty jar. A client with no address keeps the pooled client that the application's in-process transport replaces.

## How to use it

An initializer creates one named client for a test and registers it:

```csharp
public interface IProtoClientInitializer
{
    string Name { get; }
    string? Protocol => null;
    Type ClientType { get; }
    Task<bool> TryInitializeAsync(ProtoExecutionContext context);
}

public interface IProtoClientInitializer<TClient> : IProtoClientInitializer where TClient : class
{
    // ClientType is implemented for you
}
```

From the sample suite:

```csharp
public sealed class ScenarioProbe
{
    private readonly List<string> _milestones = [];
    public IReadOnlyList<string> Milestones => _milestones;
    public void Mark(string milestone) => _milestones.Add(milestone);
}

public sealed class ScenarioProbeInitializer : IProtoClientInitializer<ScenarioProbe>
{
    public string Name => "ScenarioProbe";

    public Task<bool> TryInitializeAsync(ProtoExecutionContext context)
    {
        context.RegisterClient(new ScenarioProbe(), Name);
        return Task.FromResult(true);
    }
}
```

```csharp
builder.ConfigureServices(services =>
    services.AddSingleton<IProtoClientInitializer, ScenarioProbeInitializer>());
```

Then take it from the context in a test:

```csharp
var probe = Proto.Context.Client<ScenarioProbe>("ScenarioProbe");
probe.Mark("order-created");
```

A typed accessor is one extension method away, which is exactly what `Rest()` and `Web()` are:

```csharp
public static class ScenarioProbeExtensions
{
    public static ScenarioProbe Probe(this ProtoExecutionContext context) =>
        context.Client<ScenarioProbe>("ScenarioProbe");
}
```

### Sharing one client across tests

Some clients are expensive to create: a started application, a connection pool, a container. Create them once, keep them in the initializer or a singleton service, and register them per test **without** handing over ownership:

```csharp
public sealed class SharedBusInitializer : IProtoClientInitializer<BusConnection>, IAsyncDisposable
{
    private readonly Lazy<BusConnection> _connection = new(BusConnection.Open);

    public string Name => "Bus";

    public Task<bool> TryInitializeAsync(ProtoExecutionContext context)
    {
        context.RegisterClient(_connection.Value, Name, ProtoClientOwnership.Caller);
        return Task.FromResult(true);
    }

    public ValueTask DisposeAsync() =>
        _connection.IsValueCreated ? _connection.Value.DisposeAsync() : ValueTask.CompletedTask;
}
```

```csharp
builder.ConfigureServices(services =>
    services.AddSingleton<IProtoClientInitializer>(_ => new SharedBusInitializer()));
```

With `ProtoClientOwnership.Caller` the test registers the client as shared and does not dispose it at teardown. The trace marks the entity `client.owned = false`, and the release writes state, not a dispose. Registering through a factory delegate, as above, lets the host's service provider dispose the initializer, and with it the shared client, when the run ends. Remember that tests running in parallel use a shared client concurrently.

| Ownership | Teardown | Trace |
| --- | --- | --- |
| `Context` (the default) | the test disposes the client, in reverse registration order | `client.owned` true, `resource.state` released |
| `Caller` (shared) | the test leaves it alone; the owner disposes it with the run | `client.owned` false, release writes state, not a dispose |

## What the trace shows

<TraceAnatomy
  source={lessonTraces.firstJourney}
  title="Six clients, one layer"
  test="Northstar.ProtoTest.ProjectsJourney.CreatingAProjectReturnsIt"
  layers={clientLayers}
  blindSpots={[]}
/>

- One `client.initialize` operation per (protocol, type, name) group, with the winning initializer recorded as the client entity's `client.initializer` state. A group with no winner fails the operation.
- A failed `Client<T>` lookup writes a `client.resolve` event before it throws. `TryClient` never traces.
- A successful release is the client entity's `resource.state = released` with `client.owned` set by the registration. Only a failed release writes a `resource.release` operation.
- An initializer that throws fails the `client.initialize` operation, and the test fails in setup.

## Limits

- Initializers are singleton services: a shared client must tolerate concurrent tests, and a per-test client must still be created fresh inside `TryInitializeAsync`.
- Ordering is registration order only; there is no `Order` property on an initializer.
- A group with no successful initializer fails the test's setup. There is no silent fallback.
- Registration after the context starts releasing throws, so a client can only be registered during setup or while the test body runs.
