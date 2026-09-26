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

IProtoHostBuilder AddInfrastructureAlways(
    this IProtoHostBuilder builder,
    IProtoInfrastructure infrastructure,
    params string[] settings);
```

Every key in `settings` receives the started connection string, so one started container can feed the tests and an in-process application under the configuration roots each of them reads:

```csharp
builder.AddInfrastructure(
    RabbitMqBroker.Container(),
    RabbitMqOptions.ConnectionStringSetting,   // "ProtoTest:Messaging:RabbitMq:ConnectionString"
    "Messaging:RabbitMq:ConnectionString");         // the in-process application's key
```

Passing keys requires a piece that provides addresses — an `IProtoConnectionInfrastructure` (a connection string per key) or an `IProtoSettingsInfrastructure` (the values it will fill). A settings-only piece that declares no keys always starts.

## When the environment already provides the addresses

The host decides before starting anything: a registration whose every declared key already has a configured value is not started. The environment has the address the piece would fill, so a container or process would only shadow it.

```csharp
// ConnectionStrings:Northstar is configured - environment variables, user secrets,
// appsettings in the runner project, an earlier configuration source. The container is not needed.
builder.AddInfrastructure(PostgresDatabase.Container(), "ConnectionStrings:Northstar");
```

The rule is per piece and all-or-nothing: one unconfigured key means the piece starts and fills all of its keys, and a piece with no keys always starts. Use `AddInfrastructureAlways` when a piece must start regardless:

```csharp
builder.AddInfrastructureAlways(LocalRelay.Sidecar(), "Relay:Url");
```

A skipped piece is not started, not owned and not released, and its values stay absent from `ProtoInfrastructureSettings`; readers that resolve an address at use time find the configured value in `IConfiguration`. The trace records the decision on the piece's run entity with `infrastructure.state: skipped` and `infrastructure.reason: already configured`.

## The demo's two real flows

The sample suite registers exactly these two pieces of infrastructure:

```csharp
// Broker: one key for the messaging adapter, one for the in-process application.
builder.AddInfrastructure(
    RabbitMqBroker.Container(),
    RabbitMqOptions.ConnectionStringSetting,
    "Messaging:RabbitMq:ConnectionString");

// Database: the application's key, and the same key the test-side domain reads.
builder.AddInfrastructure(PostgresDatabase.Container(), "ConnectionStrings:Northstar");
```

Both containers are started only when the suite opted in (`ProtoTest:Messaging:Broker=container`, `ProtoTest:Database=postgres`), which is what makes [one suite run in three environments](../getting-started/environments.md).

The demo also registers a settings-only resource with no keys:

```csharp
builder.AddInfrastructure(new StandaloneSampleApp(fallbackDatabase, databaseProvider));
```

It starts the sample application as a standalone process and fills `ProtoTest:Applications:{application}:BaseUrl` from its `Settings`, so the [web sessions](../integrations/web/index.md) have an address. A settings-only infrastructure fills its dictionary whether or not keys were passed.

## When it starts

`ProtoHost.StartAsync` runs in this order:

1. the **run hooks** — `BeforeRunAsync`, ascending `Order`;
2. the host's **capabilities**, recorded as run entities;
3. each **infrastructure** registration, in registration order — a piece every declared key of which is already configured is recorded as skipped instead — `StartAsync` on each piece, then its settings;
4. trace listening begins.

Runner assembly setups call `StartAsync` before any test (see the [runner overview](../runners/overview.md)), so infrastructure is guaranteed to be started and its settings filled before the first test lifecycle begins. Each started piece is recorded in the trace as a run entity with `change: "started"` and its settings keys.

If a registered piece throws during `StartAsync`, the run fails to start: `ProtoHost.StartAsync` releases the pieces that had already started, clears the settings it filled, and rethrows - leaving the host in `Created` for a retry. A retry starts the released pieces again, and each piece it starts again is released with its new ownership period; a piece the retry never restarts is not released a second time. The runner's host lifetime then disposes the host. The container packages offer `TryStart`, which reports *why* a container could not start instead of throwing, so a suite can fall back or decide to [skip](./skip-conditions.md) before registering it.

## Wait until it is ready

A running container is not necessarily serving, and a published application may still be coming up. Readiness probes replace the sleep at the top of setup:

```csharp
builder
    .AddInfrastructure(PostgresDatabase.Container(), "ConnectionStrings:Northstar")
    .AddReadinessProbe("Northstar API", ProtoReadiness.Http(new Uri("http://localhost:5080/health")))
    .ConfigureReadiness(readiness =>
    {
        readiness.Timeout = TimeSpan.FromSeconds(60);
        readiness.Interval = TimeSpan.FromMilliseconds(200);
    });
```

- A probe is infrastructure: the host awaits it at its registration position, and it is recorded as a `readiness` run entity carrying the attempts and the wait it spent.
- `ProtoReadiness.Tcp(host, port)` is ready when a connection succeeds. `ProtoReadiness.Http(url)` is ready when the address answers at all - pass an acceptance check to demand a status or a health payload. Any delegate returning `ValueTask<bool>` works too.
- An exception is "not ready yet": a connection refusal while a container boots is normal, and the last error appears in the timeout failure. The default timeout is 30 seconds.
- One policy governs every wait: `ConfigureReadiness` sets the timeout and interval for host probes **and** for the containers the run starts, and the section `ProtoTest:Readiness` binds over the code values when the host is built. A slow image is tuned in one place.
- A probe that never becomes ready fails the run before the first test, naming the probe, its attempts and the last error.

The shipped containers declare their own checks: a [PostgreSQL container](../integrations/sql/index.md) waits for its standard port to accept connections, [RabbitMQ](../integrations/messaging/index.md) for the AMQP port. When a custom image listens elsewhere, override the port:

```csharp
builder.AddInfrastructure(PostgresDatabase.Container().ReadyOn(5433), "ConnectionStrings:Northstar");
```

A published application is waited for where its address is declared - `ProtoTest:Applications:{application}:BaseUrl`, or the address a settings piece published:

```csharp
builder.AddHttpReadiness("Northstar API");
```

Register the probe **after** the piece that publishes the address: probes are awaited at their registration position, so a probe registered first resolves nothing and records `readiness.skipped` naming its position and the later publisher instead of waiting - it never claims the application runs in-process. An in-process application has no address to wait for, so the probe is skipped and records why.

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

One application-setting precedence is shared by every address reader: an address a started piece published through `ProtoInfrastructureSettings` wins over static configuration. The in-process web host and the RabbitMQ adapter decide their own settings differently, and `AddAspNetCoreServer`'s step-aside deliberately reads static configuration only:

| Reader | Order | Winner |
| --- | --- | --- |
| Application address (REST, GraphQL, gRPC, readiness, web sessions, device clients) | published infrastructure settings first, then configuration | the started instance's address |
| In-process application (`AddAspNetCoreServer`, its own settings) | infrastructure settings first, then `configureWebHost` | explicit `configureWebHost` |
| RabbitMQ adapter (`UseRabbitMq`) | `ProtoTest:Messaging:RabbitMq:ConnectionString` first, then infrastructure settings | explicit configuration |

`AddAspNetCoreServer`'s **step-aside** is the one deliberate asymmetry: it reads static configuration, so an application whose address only a started piece published keeps its in-process server (for `ServerFactory`-style access) while the address readers above talk to the published process. Give the published process its own application name when both must coexist; the demo registers its standalone console as its own application for exactly that reason.

## When it is released

Infrastructure is released with the run, after the run stops and the reports are written:

- the **run gates** evaluate first, so they see the collected items before the reports export;
- the **report sinks** export next;
- the **run resource hook** then releases run-scoped resources, infrastructure included, and writes the release into the trace;
- finally the trace archive is written.

`ProtoHost.DisposeAsync` releases anything still registered before disposing the service provider, so a host disposed without an explicit stop still cleans up. Each piece is released at most once — `ProtoContainerResource` guards both start and release — and a failed release is reported as a release failure instead of disappearing.

## `AddResource` versus `AddInfrastructure`

`IProtoHostBuilder.AddResource(IProtoResource)` adds a run-scoped resource and nothing else: the host does **not** call `StartAsync` (a plain resource has none) and does **not** fill any settings. The resource is still recorded as owned and released with the run.

| | `AddInfrastructure` | `AddResource` |
| --- | --- | --- |
| Starts with the run | yes, unless every declared key is already configured (`AddInfrastructureAlways` opts out) | no |
| Fills `ProtoInfrastructureSettings` | connection string per key, plus settings | no |
| Registered as run entity and released | yes | yes |

Use `AddInfrastructure` when the piece must start with the run or publish values; use `AddResource` for an already-started handle that only needs the lifecycle.

## Limits

- **Once per run.** Infrastructure starts and stops at run boundaries. Per-test setup is a [hook or attribute](./hooks.md) job.
- **A started container is not watched.** The host waits for readiness once, at run start, and never polls the container again: if a container dies mid-run, the next call through its published connection string fails with the transport's own error in that test, and the run's release disposes what is left. There is no restart, failover or liveness probe, and the published setting keeps the dead address until the run is released.
- **A configured environment wins.** Declare the keys a piece fills; when all of them are configured the host skips the piece instead of shadowing the environment. `AddInfrastructureAlways` forces a start (see [above](#when-the-environment-already-provides-the-addresses)).
- **Failures are run failures.** There is no automatic skip for infrastructure that cannot start; use `TryStart` and decide before registering.
- **Readiness fails, it does not skip.** A probe that times out fails the run before the first test. It also runs once, at run start - waiting inside a test is a hook's job, not a probe's.
- **Settings don't change `IConfiguration`.** They live in `ProtoInfrastructureSettings`; a reader that only looks at `IConfiguration` won't see them. The built-in readers — the address readers (REST/GraphQL/gRPC clients, readiness, web sessions, device clients), the in-process web host and the RabbitMQ adapter — do.
- **No ordering control.** Registrations start in the order they were added, and there is no dependency graph between pieces.
- **`ProtoInfrastructureSettings.Set` is internal.** Only the host fills it; tests read `Values`.
