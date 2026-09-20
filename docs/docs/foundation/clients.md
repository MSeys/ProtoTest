---
sidebar_position: 5
title: Clients
description: "Clients are what a test talks to. ProtoTest creates them per test, registers them on the context and disposes them afterwards."
---

# Clients

A client is anything a test talks to — an `HttpClient`, a browser session, a message bus connection, a fake. ProtoTest creates clients **per test**, registers them on the context, and disposes them afterwards. Most integrations use this mechanism for the systems they connect to, and you can use it for your own.

## The context API

```csharp
void RegisterClient<TClient>(TClient client, string name = "Default", bool disposeWithContext = true)
    where TClient : class;
TClient Client<TClient>(string name = "Default") where TClient : class;
TClient? TryClient<TClient>(string name = "Default") where TClient : class;
```

Clients are keyed by **type and case-insensitive name**: an `HttpClient` named `Api` and a web session named `Api` can coexist. Registering the same type and name twice throws, and registering after release has begun throws `ObjectDisposedException`. A failed `Client<T>` lookup records a `client.resolve` event before throwing; `TryClient` never traces.

## Writing an initializer

An initializer creates one named client for a test and registers it:

```csharp
public interface IProtoClientInitializer
{
    string Name { get; }
    Type ClientType { get; }
    Task<bool> TryInitializeAsync(ProtoExecutionContext context, CancellationToken cancellationToken = default);
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

    public Task<bool> TryInitializeAsync(ProtoExecutionContext context, CancellationToken cancellationToken = default)
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

```csharp
var probe = Proto.Context.Client<ScenarioProbe>("ScenarioProbe");
probe.Mark("order-created");
```

A nicer API is one extension method away — which is exactly what `Rest()` and `Web()` are:

```csharp
public static class ScenarioProbeExtensions
{
    public static ScenarioProbe Probe(this ProtoExecutionContext context) =>
        context.Client<ScenarioProbe>("ScenarioProbe");
}
```

## When clients are created and released

Before any of your hooks or attributes run, ProtoTest's client hook groups every registered initializer by client type and name, tries each group in registration order, and creates its client. The trace records one `client.initialize` operation per group — not one per attempt — and the winning initializer is written as the client entity's `client.initializer` state. A candidate that returns `false` is not traced individually; an initializer that throws fails the `client.initialize` operation.

If no initializer in a group succeeds, the test fails in setup with *"No registered initializer could create a client of type 'X' with name 'Y'."*

If a client implements `IDisposable` or `IAsyncDisposable`, it's disposed when the test ends — in reverse order of registration. Client resources are framework-managed: a successful release is recorded as the client entity's `resource.state = released` rather than a `resource.release` operation, and a **failed** release writes a `resource.release` event so the failure is explainable.

## Sharing one client across tests

Some clients are expensive to create — a started application, a connection pool, a container. Create them once, keep them in the initializer (or a singleton service), and register them per test **without** handing over ownership:

```csharp
public sealed class SharedBusInitializer : IProtoClientInitializer<BusConnection>, IAsyncDisposable
{
    private readonly Lazy<BusConnection> _connection = new(BusConnection.Open);

    public string Name => "Bus";

    public Task<bool> TryInitializeAsync(ProtoExecutionContext context, CancellationToken cancellationToken = default)
    {
        context.RegisterClient(_connection.Value, Name, disposeWithContext: false);
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

With `disposeWithContext: false` the test registers the client as shared and does not dispose it at teardown — the trace marks the entity `client.owned = false`, and the release writes state, not a dispose. Registering through a factory delegate, as above, lets the host's service provider dispose the initializer — and with it the shared client — when the run ends. This is how the [ASP.NET Core integration](../integrations/aspnetcore.md) shares one application across tests. Remember that tests running in parallel will use a shared client concurrently.

## Fallback chains

Several initializers can offer the same client type and name. They're tried **in registration order**, and the first to return `true` wins. Returning `false` means "not me" — and the initializer must leave the context untouched when it does.

This is how a client bound to an application is served by a real URL when the application has a `BaseUrl`, and by the [in-process ASP.NET Core server](../integrations/aspnetcore.md) otherwise:

```csharp
public async Task<bool> TryInitializeAsync(ProtoExecutionContext context, CancellationToken cancellationToken = default)
{
    var url = context.Configuration[$"ProtoTest:Applications:{Name}:BaseUrl"];
    if (url is null) return false;           // let the next initializer try

    context.RegisterClient(new HttpClient { BaseAddress = new Uri(url) }, Name);
    return true;
}
```

## Limits

- Initializers are singleton services: a shared client must tolerate concurrent tests, and a per-test client must still be created fresh inside `TryInitializeAsync`.
- Ordering is registration order only; there is no `Order` property on an initializer.
- A group with no successful initializer fails the test's setup — there is no silent fallback.
- Registration after the context starts releasing throws, so a client can only be registered during setup (or while the test body runs).
