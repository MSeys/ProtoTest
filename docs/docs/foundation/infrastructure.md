---
sidebar_position: 10
title: Infrastructure
description: "Declare a database, broker or emulator on the host; it starts once before any test, fills configuration, and is released with the run."
---

# Infrastructure

Infrastructure is what the run provides for itself: a database, a broker, a storage emulator. Declare it on the host builder, and the host starts it once before any test, records it, and releases it with the run — no `BeforeRun` hook, no manual start and stop.

```csharp
builder.AddInfrastructure(PostgresDatabase.Container(), "ConnectionStrings:Northstar");
```

## What counts as infrastructure

```csharp
public interface IProtoInfrastructure : IProtoResource
{
    ValueTask StartAsync(CancellationToken cancellationToken = default);
}

public interface IProtoConnectionInfrastructure : IProtoInfrastructure
{
    string ConnectionString { get; }
}

public interface IProtoSettingsInfrastructure : IProtoInfrastructure
{
    IReadOnlyDictionary<string, string> Settings { get; }
}
```

- `IProtoInfrastructure` is anything the host starts with the run. It is an `IProtoResource`, so it is also recorded as a run entity and released at the end.
- `IProtoConnectionInfrastructure` provides a single `ConnectionString` — a database or broker the application and the tests both connect to.
- `IProtoSettingsInfrastructure` provides arbitrary `Settings` values — for example the address of a standalone application the run started.

Implementations must be run-scoped: `AddInfrastructure` rejects anything whose `Scope` is not `ProtoResourceScope.Run`, because infrastructure outlives a test.

## Registering

```csharp
IProtoHostBuilder AddInfrastructure(
    this IProtoHostBuilder builder,
    IProtoInfrastructure infrastructure,
    params string[] settings);
```

Every key in `settings` receives the started connection string, so one started container can feed the tests and an in-process application under the configuration roots each of them reads:

```csharp
builder.AddInfrastructure(
    RabbitMqBroker.Container(),
    ProtoRabbitMqOptions.ConnectionStringSetting,   // "ProtoTest:Messaging:RabbitMq:ConnectionString"
    "Messaging:RabbitMq:ConnectionString");         // the in-process application's key
```

Passing keys requires an `IProtoConnectionInfrastructure`; a settings-only resource keeps its own `Settings` dictionary and doesn't need keys.

## The demo's two real flows

The sample suite registers exactly these two pieces of infrastructure:

```csharp
// Broker: one key for the messaging adapter, one for the in-process application.
builder.AddInfrastructure(
    RabbitMqBroker.Container(),
    ProtoRabbitMqOptions.ConnectionStringSetting,
    "Messaging:RabbitMq:ConnectionString");

// Database: the application's key, and the same key the test-side domain reads.
builder.AddInfrastructure(PostgresDatabase.Container(), "ConnectionStrings:Northstar");
```

Both containers are started only when the suite opted in (`ProtoTest:Messaging:Broker=container`, `ProtoTest:Database=postgres`), which is what makes [one suite run in three environments](../getting-started/environments.md).

The demo also registers a settings-only resource with no keys:

```csharp
builder.AddInfrastructure(new StandaloneSampleApp(fallbackDatabase));
```

It starts the sample application as a standalone process and fills `ProtoTest:Web:Sessions:Default:BaseUrl` from its `Settings`, so the [web sessions](../integrations/web/index.md) have an address. A settings-only infrastructure fills its dictionary whether or not keys were passed.

## When it starts

`ProtoHost.StartAsync` runs in this order:

1. the **run hooks** — `BeforeRunAsync`, ascending `Order`;
2. the host's **capabilities**, recorded as run entities;
3. each **infrastructure** registration, in registration order — `StartAsync` on each piece, then its settings;
4. trace listening begins.

Runner assembly setups call `StartAsync` before any test (see the [runner overview](../runners/overview.md)), so infrastructure is guaranteed to be started and its settings filled before the first test lifecycle begins. Each started piece is recorded in the trace as a run entity with `change: "started"` and its settings keys.

If a registered piece throws during `StartAsync`, the run fails to start and the host disposes itself; pieces that had already started are released. The container packages offer `TryStart`, which reports *why* a container could not start instead of throwing, so a suite can fall back or decide to [skip](./skip-conditions.md) before registering it.

## How settings reach tests

The host fills the single `ProtoInfrastructureSettings` instance for the run. Its `Values` is a snapshot of key/value pairs, and it is an ordinary service:

```csharp
var values = Proto.Context.TryService<ProtoInfrastructureSettings>()?.Values;
var connection = values?["ConnectionStrings:Northstar"];
```

The sample suite's `Setup` reads it while composing the test-side domain, so the domain connects to the same database as the application.

An **in-process application** receives the same keys automatically. The ASP.NET Core initializer applies every infrastructure setting with `webHost.UseSetting(key, value)` before your `configureWebHost` callback runs, so explicit code wins:

```csharp
app.AddAspNetCoreServer<Program>(configureWebHost: webHost =>
{
    // Infrastructure settings are already applied here; this call overrides one of them.
    webHost.UseSetting("ConnectionStrings:Northstar", "Data Source=override.db");
});
```

Precedence is decided by each reader. The in-process web host and the RabbitMQ adapter both apply explicit configuration last, so it wins; the web session is the deliberate exception:

| Reader | Order | Winner |
| --- | --- | --- |
| In-process application (`AddAspNetCoreServer`) | infrastructure settings first, then `configureWebHost` | explicit `configureWebHost` |
| RabbitMQ adapter (`UseRabbitMq`) | `ProtoTest:Messaging:RabbitMq:ConnectionString` first, then infrastructure settings | explicit configuration |
| Web session base URL | infrastructure-provided `ProtoTest:Web:Sessions:{name}:BaseUrl` first, then configuration | the started instance's address |

The web session is the deliberate exception: the address of the process the run started wins over a configured one, so a standalone instance is always the one the browser drives.

## When it is released

Infrastructure is released with the run, after the run stops and the reports are written:

- report sinks and run gates run as `AfterRunAsync` hooks first;
- the **run resource hook** then releases run-scoped resources, infrastructure included, and writes the release into the trace;
- finally the trace archive is written.

`ProtoHost.DisposeAsync` releases anything still registered before disposing the service provider, so a host disposed without an explicit stop still cleans up. Each piece is released at most once — `ProtoContainerResource` guards both start and release — and a failed release is reported as a release failure instead of disappearing.

## `AddResource` versus `AddInfrastructure`

`IProtoHostBuilder.AddResource(IProtoResource)` adds a run-scoped resource and nothing else: the host does **not** call `StartAsync` (a plain resource has none) and does **not** fill any settings. The resource is still recorded as owned and released with the run.

| | `AddInfrastructure` | `AddResource` |
| --- | --- | --- |
| Starts with the run | yes, `StartAsync` | no |
| Fills `ProtoInfrastructureSettings` | connection string per key, plus settings | no |
| Registered as run entity and released | yes | yes |

Use `AddInfrastructure` when the piece must start with the run or publish values; use `AddResource` for an already-started handle that only needs the lifecycle.

## Limits

- **Once per run.** Infrastructure starts and stops at run boundaries. Per-test setup is a [hook or attribute](./hooks.md) job.
- **Failures are run failures.** There is no automatic skip for infrastructure that cannot start; use `TryStart` and decide before registering.
- **Settings don't change `IConfiguration`.** They live in `ProtoInfrastructureSettings`; a reader that only looks at `IConfiguration` won't see them. The built-in readers — the in-process web host, the RabbitMQ adapter and web sessions — do.
- **No ordering control.** Registrations start in the order they were added, and there is no dependency graph between pieces.
- **`ProtoInfrastructureSettings.Set` is internal.** Only the host fills it; tests read `Values`.
