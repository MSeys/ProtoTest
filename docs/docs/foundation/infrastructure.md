---
sidebar_position: 10
title: Infrastructure
description: "Declare a database, broker or emulator on the host; it starts once before any test, fills configuration, and is released with the run."
---

# Infrastructure

## What it is

Infrastructure is what the run provides for itself: a database, a broker, a storage emulator. Declare it on the host builder, and the host starts it once before any test, records it, and releases it with the run. No `BeforeRun` hook, no manual start and stop.

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
- `IProtoConnectionInfrastructure` provides a single `ConnectionString` for a database or broker the application and the tests both connect to.
- `IProtoSettingsInfrastructure` provides arbitrary `Settings` values, for example the address of a standalone application the run started.

Implementations must be run-scoped: `AddInfrastructure` rejects anything whose `Scope` is not `ProtoResourceScope.Run`, because infrastructure outlives a test.

## How it works

### Registering a target

```csharp
IProtoHostBuilder AddInfrastructure(
    this IProtoHostBuilder builder,
    string name,
    Action<IProtoProviderChainBuilder> configure,
    params string[] keys);
```

Register a **target** once with its **providers** in priority order and the keys every provider checks or fills:

```csharp
builder.AddInfrastructure(
    "NorthstarDatabase",
    chain => chain
        .UseConfigured()                        // ConnectionStrings:Northstar is set: the environment serves it
        .UseContainer(PostgresDatabase.Container()),
    "ConnectionStrings:Northstar");
```

The first provider whose condition holds serves the target, and only its piece starts and fills every declared key. A container can feed the tests and an in-process application under the configuration roots each of them reads:

```csharp
builder.AddInfrastructure(
    "MessagingBroker",
    chain => chain
        .UseConfigured()
        .UseContainer(RabbitMqBroker.Container()),
    RabbitMqOptions.ConnectionStringSetting,   // "ProtoTest:Messaging:RabbitMq:ConnectionString"
    "Messaging:RabbitMq:ConnectionString");    // the in-process application's key
```

Declaring keys requires providers whose pieces provide addresses: an `IProtoConnectionInfrastructure` (a connection string per key) or an `IProtoSettingsInfrastructure` (the values it will fill). A target that declares no key serves its winner's piece unconditionally, so give such a provider no condition. [Environment resolution](./environment-resolution.md) has the full provider contract, the conditions and the trace record.

`AddInfrastructure(piece, keys)` is the legacy single-piece registration. Its implicit rule is that the piece is not started when every declared key is already configured, and `AddInfrastructureAlways` keeps its always-start opt-out. It is **obsolete** since the chain seam landed and keeps working for 1.x. Migrate a call by moving the piece into a chain: `chain.UseConfigured().Use(new ProtoTargetProvider("piece", piece))` is the exact replacement, and a provider with no condition replaces `AddInfrastructureAlways`.

```csharp
// Legacy: the piece starts even when Relay:Url is configured.
builder.AddInfrastructureAlways(LocalRelay.Sidecar(), "Relay:Url");
```

### When the environment already provides the addresses

`UseConfigured()` is the provider that holds when every key the **target** declares already has a configured value. The environment has the address the piece would fill, so a container or process would only shadow it:

```csharp
builder.AddInfrastructure(
    "NorthstarDatabase",
    chain => chain
        .UseConfigured()
        .UseContainer(PostgresDatabase.Container()),
    "ConnectionStrings:Northstar");
```

When `ConnectionStrings:Northstar` is configured, through environment variables, user secrets, appsettings in the runner project or an earlier configuration source, `UseConfigured()` wins and the container provider never starts.

The rule reads the target's keys all at once: one unconfigured key means the provider after `UseConfigured()` serves the target and fills all of the keys, and a provider with no condition always wins when nothing before it did. A provider that loses is not started, not owned and not released, and its values stay absent from `ProtoInfrastructureSettings`. Readers that resolve an address at use time find the configured value in `IConfiguration`. The trace records the resolution with `environment.resolved`, and the piece's run entity as `infrastructure.state: skipped` with the reason. A registration without a chain (the obsolete overload) records the single decision as `infrastructure.reason: already configured`.

The chain is the one registration shape: the configured environment, a container, an AppHost resource and a published address are providers of the same target, and the first whose condition holds wins. Only the winner's piece starts and declares its capabilities; the others are recorded skipped with the reason, and a target no provider can serve fails the build naming every unmet condition.

### When it starts

`ProtoHost.StartAsync` runs in this order:

1. the **run hooks**: `BeforeRunAsync`, ascending `Order`;
2. the host's **capabilities**, recorded as run entities;
3. each **infrastructure** registration, in registration order. A losing chain provider is never started, and an obsolete no-chain piece whose every declared key is already configured is recorded as skipped instead. `StartAsync` runs on each piece, then its settings. **Run setup steps** (`AddRunSetup`) are infrastructure too, so a step starts after the pieces registered before it;
4. trace listening begins.

Runner assembly setups call `StartAsync` before any test (see the [runner overview](../runners/overview.md)), so infrastructure is guaranteed to be started and its settings filled before the first test lifecycle begins. Each started piece is recorded in the trace as a run entity with `change: "started"` and its settings keys.

If a registered piece throws during `StartAsync`, the run fails to start: `ProtoHost.StartAsync` releases the pieces that had already started, clears the settings it filled, and rethrows, leaving the host in `Created` for a retry. A retry starts the released pieces again, and each piece it starts again is released with its new ownership period; a piece the retry never restarts is not released a second time. The runner's host lifetime then disposes the host. The container packages offer `TryStart`, which reports *why* a container could not start instead of throwing, so a suite can fall back or decide to [skip](./skip-conditions.md) before registering it.

### How settings reach tests

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

The address readers share one precedence: an address a started piece published through `ProtoInfrastructureSettings` wins over static configuration. The in-process web host and the RabbitMQ adapter decide their own settings differently, and `AddAspNetCoreServer`'s step-aside deliberately reads static configuration only:

| Reader | Order | Winner |
| --- | --- | --- |
| Application address (REST, GraphQL, gRPC, readiness, web sessions, device clients) | published infrastructure settings first, then configuration | the started instance's address |
| In-process application (`AddAspNetCoreServer`, its own settings) | infrastructure settings first, then `configureWebHost` | explicit `configureWebHost` |
| RabbitMQ adapter (`UseRabbitMq`) | `ProtoTest:Messaging:RabbitMq:ConnectionString` first, then infrastructure settings | explicit configuration |

`AddAspNetCoreServer`'s **step-aside** is the one deliberate asymmetry: it reads static configuration, so an application whose address only a started piece published keeps its in-process server, for `ServerFactory`-style access, while the address readers above talk to the published process. Give the published process its own application name when both must coexist. The demo registers its standalone console as its own application for exactly that reason.

## How to use it

### The sample's two flows

The sample suite registers its two container-backed targets:

```csharp
// Broker: one key for the messaging adapter, one for the in-process application.
builder.AddInfrastructure(
    "MessagingBroker",
    chain => chain
        .UseConfigured()
        .UseContainer(RabbitMqBroker.Container()),
    RabbitMqOptions.ConnectionStringSetting,
    "Messaging:RabbitMq:ConnectionString");

// Database: the application's key, and the same key the test-side domain reads.
builder.AddInfrastructure(
    "NorthstarDatabase",
    chain => chain
        .UseConfigured()
        .UseContainer(PostgresDatabase.Container()),
    "ConnectionStrings:Northstar");
```

Both containers are started only when the suite opted in (`ProtoTest:Messaging:Broker=container`, `ProtoTest:Database=postgres`), which is what makes [one suite run in three environments](../getting-started/environments.md).

The browser journey's address comes from the application chain, not a container target. The sample registers its web application's loopback provider, which starts `NorthstarProgram.CreateApp` on a real listener and publishes the bound address as `ProtoTest:Applications:Northstar web:BaseUrl`, so the [web sessions](../integrations/web/index.md) follow it. `AddLoopbackApplication(name, createApp)` is the convenience form. [Hosting a browser journey](../integrations/aspnetcore.md#hosting-a-browser-journey) has the recipe, including `UseLoopback(createApp)` when the listener is one provider among others.

An application image is the container counterpart: `ApplicationContainer` (`ProtoTest.Testcontainers`) starts the image as run infrastructure and fills `ProtoTest:Applications:{application}:BaseUrl` from the mapped address, so the application's clients, browser sessions and readiness probe resolve it:

```csharp
var api = ApplicationContainer.Container("Api", "my-registry.example.test/orders-api:1.4", port: 8080);
builder.AddInfrastructure(
    "OrdersApi",
    chain => chain
        .UseConfigured()
        .UseContainer(api),
    api.BaseUrlKey);
```

Register it instead of `AddAspNetCoreServer` for that application: a containerized application has no in-process server. [Hosting a browser journey](../integrations/aspnetcore.md#hosting-a-browser-journey) shows the full composition next to the loopback recipe.

### Wait until it is ready

A running container is not necessarily serving, and a published application may still be coming up. Readiness probes replace the sleep at the top of setup:

```csharp
builder
    .AddInfrastructure(
        "NorthstarDatabase",
        chain => chain.UseContainer(PostgresDatabase.Container()),
        "ConnectionStrings:Northstar")
    .AddReadinessProbe("Northstar API", ProtoReadiness.Http(new Uri("http://localhost:5080/health")))
    .ConfigureReadiness(readiness =>
    {
        readiness.Timeout = TimeSpan.FromSeconds(60);
        readiness.Interval = TimeSpan.FromMilliseconds(200);
    });
```

- A probe is infrastructure: the host awaits it at its registration position, and it is recorded as a `readiness` run entity carrying the attempts and the wait it spent.
- `ProtoReadiness.Tcp(host, port)` is ready when a connection succeeds. `ProtoReadiness.Http(url)` is ready when the address answers at all; pass an acceptance check to demand a status or a health payload. Any delegate returning `ValueTask<bool>` works too.
- An exception is "not ready yet": a connection refusal while a container boots is normal, and the last error appears in the timeout failure. The default timeout is 30 seconds.
- One policy governs every wait: `ConfigureReadiness` sets the timeout and interval for host probes **and** for the containers the run starts, and the section `ProtoTest:Readiness` binds over the code values when the host is built. A slow image is tuned in one place.
- A probe that never becomes ready fails the run before the first test, naming the probe, its attempts and the last error.

The shipped containers declare their own checks: a [PostgreSQL container](../integrations/sql/index.md) waits for its standard port to accept connections, [RabbitMQ](../integrations/messaging/index.md) for the AMQP port. When a custom image listens elsewhere, override the port:

```csharp
builder.AddInfrastructure(
    "NorthstarDatabase",
    chain => chain.UseContainer(PostgresDatabase.Container().ReadyOn(5433)),
    "ConnectionStrings:Northstar");
```

A published application is waited for where its address is declared, in `ProtoTest:Applications:{application}:BaseUrl` or the address a settings piece published:

```csharp
builder.AddHttpReadiness("Northstar API");
```

Register the probe **after** the piece that publishes the address. Probes are awaited at their registration position, so a probe registered first resolves nothing and records `readiness.skipped` naming its position and the later publisher instead of waiting. It never claims the application runs in-process. An in-process application has no address to wait for, so the probe is skipped and records why.

### Run-scoped setup

Some run-owned state is an action rather than a piece to own: create the schema of a container database, seed a catalogue, warm a cache. `AddRunSetup(name, delegate)` runs it once at the run's start, at its registration position in the infrastructure order, so a step registered after a container reads the connection string that container published:

```csharp
builder
    .AddInfrastructure(
        "NorthstarDatabase",
        chain => chain
            .UseConfigured()
            .UseContainer(PostgresDatabase.Container()),
        "ConnectionStrings:Northstar")
    .AddSql(
        provider => new NpgsqlConnection(ResolveDatabase(provider, "ConnectionStrings:Northstar")),
        sql => sql.AddressKeys.Add("ConnectionStrings:Northstar"))
    .AddEntityFrameworkCore<OrdersDbContext>((services, options) =>
        options.UseNpgsql(services.GetRequiredService<DbConnection>()))
    .AddRunSetup("database schema", async setup =>
    {
        var connectionString = setup.Settings.Values.TryGetValue("ConnectionStrings:Northstar", out var published)
            ? published
            : setup.Configuration["ConnectionStrings:Northstar"]
              ?? throw new InvalidOperationException(
                  "ConnectionStrings:Northstar is not configured and no container published it.");
        var options = new DbContextOptionsBuilder<OrdersDbContext>().UseNpgsql(connectionString).Options;
        await using var context = new OrdersDbContext(options);
        await context.Database.EnsureCreatedAsync(setup.CancellationToken);
    });
```

The step receives a `ProtoRunSetupContext`:

- **`Settings`**: the values the pieces registered before it published, so it reads a container's connection string without a second lookup.
- **`Configuration`**: the suite's configuration, for an environment that provides the address and makes the container skip.
- **`CancellationToken`**: the run's start token.

A step owns nothing to release. Stop and dispose release the run's resources and do not call the step again, and the run records it as an entity like any other piece. A step that throws fails the run's start with its own exception, releases what had started and leaves the host retryable, so a retry runs the step again: the same loud failure as infrastructure that cannot start.

Use a step instead of a run hook or a test setup when the state belongs to the whole run: a run hook runs before infrastructure starts and cannot see a container's address, and a test hook or test body runs inside the per-test transaction, where its DDL is rolled back with the test. The SQL page shows the [run-owned schema recipe](../integrations/sql/index.md#run-owned-schema).

### `AddResource` versus `AddInfrastructure`

`IProtoHostBuilder.AddResource(IProtoResource)` adds a run-scoped resource and nothing else: the host does **not** call `StartAsync` (a plain resource has none) and does **not** fill any settings. The resource is still recorded as owned and released with the run.

| | `AddInfrastructure` | `AddResource` |
| --- | --- | --- |
| Starts with the run | yes, the winning provider's piece (the legacy no-chain overload: unless every declared key is configured, `AddInfrastructureAlways` opts out) | no |
| Fills `ProtoInfrastructureSettings` | connection string per key, plus settings | no |
| Registered as run entity and released | yes | yes |

Use `AddInfrastructure` when the piece must start with the run or publish values. Use `AddResource` for an already-started handle that only needs the lifecycle.

## What the trace shows

- One `environment.resolved` event per target with the target, its keys, the winning provider and every skipped provider with the reason, plus one `environment.provider.skipped` event per loser.
- Each piece as a run entity: `infrastructure.state: started` with its settings keys, or `infrastructure.state: skipped` with the reason. The obsolete no-chain decision records `infrastructure.reason: already configured`.
- A readiness probe as a `readiness` run entity carrying its attempts and the wait it spent, and `readiness.skipped` when it found no address to wait for.
- A run setup step as a run entity like any other piece. A step that throws fails the run's start.
- Release runs after the reports export and before the trace archive is written. Each piece is released at most once, and a failed release is reported rather than swallowed.

## Limits

- **Once per run.** Infrastructure starts and stops at run boundaries. Per-test setup is a [hook or attribute](./hooks.md) job. `AddRunSetup` is the run-level counterpart of a setup hook: it runs once at run start, owns nothing to release, and stop and dispose do not call it again.
- **A started container is not watched.** The host waits for readiness once, at run start, and never polls the container again. If a container dies mid-run, the next call through its published connection string fails with the transport's own error in that test, and the run's release disposes what is left. There is no restart, failover or liveness probe, and the published setting keeps the dead address until the run is released.
- **A configured environment wins.** Declare the keys a target fills and put `UseConfigured()` first: when all of them are configured, the configured provider serves the target instead of a piece shadowing the environment. The legacy no-chain overload keeps the same rule for one piece, and `AddInfrastructureAlways` forces a start.
- **Failures are run failures.** There is no automatic skip for infrastructure that cannot start. Use `TryStart` and decide before registering.
- **Readiness fails, it does not skip.** A probe that times out fails the run before the first test. It also runs once, at run start; waiting inside a test is a hook's job, not a probe's.
- **Settings do not change `IConfiguration`.** They live in `ProtoInfrastructureSettings`. A reader that only looks at `IConfiguration` will not see them; the built-in readers (the address readers, the in-process web host and the RabbitMQ adapter) do.
- **No ordering control.** Registrations start in the order they were added, and there is no dependency graph between pieces.
- **`ProtoInfrastructureSettings.Set` is internal.** Only the host fills it. Tests read `Values`.
