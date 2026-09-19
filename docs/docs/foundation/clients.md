---
sidebar_position: 5
title: Clients
description: "Clients are what a test talks to. ProtoTest creates them per test, registers them on the context and disposes them afterwards."
---

# Clients

A client is anything a test talks to — an `HttpClient`, a browser session, a message bus connection, a fake. ProtoTest creates clients **per test**, registers them on the context, and disposes them afterwards. Every integration uses this mechanism, and you can use it for your own.

## Client initializers

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

An initializer creates one named client for a test and registers it with `context.RegisterClient`. From the sample suite:

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

## When clients are created

Before any of your hooks or attributes run, ProtoTest's client hook goes through every registered initializer and creates its client. So clients are available in `BeforeTestAsync` everywhere.

If a client implements `IDisposable` or `IAsyncDisposable`, it's disposed when the test ends — in reverse order of registration.

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

With `disposeWithContext: false` the test releases its reference to the client (a `resource.release` trace entry) without disposing it. Registering through a factory delegate, as above, lets the host's service provider dispose the initializer — and with it the shared client — when the run ends. This is how the [ASP.NET Core integration](../integrations/aspnetcore.md#one-application-or-one-per-test) shares one application across tests. Remember that tests running in parallel will use a shared client concurrently.

## Fallback chains

Several initializers can offer the same client type and name. They're tried **in registration order**, and the first to return `true` wins. Returning `false` means "not me" — and the initializer must leave the context untouched when it does.

This is how a client bound to an application is served by a real URL when the application has a `BaseUrl`, and by the [in-process ASP.NET Core server](../integrations/aspnetcore.md#real-server-or-in-process) otherwise:

```csharp
public async Task<bool> TryInitializeAsync(ProtoExecutionContext context, CancellationToken cancellationToken = default)
{
    var url = context.Configuration[$"ProtoTest:Applications:{Name}:BaseUrl"];
    if (url is null) return false;           // let the next initializer try

    context.RegisterClient(new HttpClient { BaseAddress = new Uri(url) }, Name);
    return true;
}
```

If no initializer succeeds, the test fails in setup with *"No registered initializer could create a client of type 'X' with name 'Y'."*

Each attempt is traced as `client.initializer.attempt`, marked with whether it was selected — so the trace tells you which one served a client.

## Names

Client names are **case-insensitive**, and a name is unique per client type: an `HttpClient` named `Api` and a `WebSession` named `Api` can coexist. Registering the same type and name twice throws.
