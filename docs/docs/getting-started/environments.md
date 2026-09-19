---
sidebar_position: 4
title: Environments
description: "Run the same suite in-process, container-backed or against a published environment, changing only the host's setup and configuration."
---

# One suite, three environments

The same suite runs in three shapes without a code change: **in-process** on a laptop, **container-backed** on a machine with a container runtime, and **published** against a deployed environment. Only the host's setup and configuration differ — the journeys, attributes, assertions and reports are the same file.

The demo's `Setup` decides all three from configuration:

```csharp
var targetUrl = demoConfiguration["ProtoTest:TargetUrl"];
var hostedInProcess = string.IsNullOrWhiteSpace(targetUrl);
var configuredDatabase = demoConfiguration.GetConnectionString("Northstar");
var usePostgres = string.Equals(demoConfiguration["ProtoTest:Database"], "postgres", StringComparison.OrdinalIgnoreCase);
var useMessagingContainer = string.Equals(demoConfiguration["ProtoTest:Messaging:Broker"], "container", StringComparison.OrdinalIgnoreCase);
```

| | In-process | Container-backed | Published |
| --- | --- | --- | --- |
| Selected by | `TargetUrl` empty | `ProtoTest:Database=postgres`, `ProtoTest:Messaging:Broker=container` | `TargetUrl` set |
| Application | `AddAspNetCoreServer<Program>` | in-process, fed by infrastructure settings | never started; clients target `BaseUrl` |
| Store | file SQLite under `TestResults/ProtoTest.Demo` | `PostgresDatabase.Container()` | `ConnectionStrings:Northstar` |
| Broker | in-memory default | `RabbitMqBroker.Container()` | `ProtoTest:Messaging:RabbitMq:ConnectionString` |
| Browser journeys | a standalone app the run starts | skipped (no standalone instance) | a standalone app the run starts (unless PostgreSQL is used) |

## In-process

With no `TargetUrl`, the host builds the application in-process and the REST, GraphQL and gRPC clients reuse its transport — no port is opened:

```csharp
app.AddAspNetCoreServer<Program>(configureWebHost: webHost =>
{
    if (!usePostgres)
    {
        webHost.UseSetting("ConnectionStrings:Northstar", fallbackDatabase);
    }

    webHost.UseSetting("Database:Provider", databaseProvider);
    webHost.UseSetting("ProtoTest:TestSupport", "true");
});
```

The demo owns a **file** SQLite database per run (`TestResults/ProtoTest.Demo/northstar-demo.db`, deleted before the run starts). The sample application itself would fall back to a named in-memory SQLite database if it were launched without a connection string, but the demo always hands it one. When the suite doesn't own PostgreSQL, it also starts a standalone copy of the application for the browser journeys and fills the default [web session](../integrations/web/index.md)'s base URL from it.

Because the test-side domain is composed over the same store, `DomainAccessJourney` runs; because no RabbitMQ adapter is configured, `MessagingJourney` skips.

## Container-backed

Setting `ProtoTest:Database=postgres` or `ProtoTest:Messaging:Broker=container` makes the run own the container through [infrastructure](../foundation/infrastructure.md):

```csharp
builder.AddInfrastructure(
    RabbitMqBroker.Container(),
    RabbitMqOptions.ConnectionStringSetting,
    "Messaging:RabbitMq:ConnectionString");

builder.AddInfrastructure(PostgresDatabase.Container(), "ConnectionStrings:Northstar");
```

The started connection strings reach the tests through `ProtoInfrastructureSettings` and the in-process application through its web host settings, so both work against the same database or broker. An explicitly configured connection string is applied afterwards and wins. With PostgreSQL owned by the run, no standalone application is started, so the browser journey's `[RequiresCapability("server", CapabilityName = "Northstar standalone")]` skips it.

## Published

Setting `ProtoTest:TargetUrl` points the suite at a deployed system:

```csharp
if (!hostedInProcess)
{
    settings[$"ProtoTest:Applications:{NorthstarTargets.Api}:BaseUrl"] = targetUrl;
}
```

No ASP.NET Core server is registered: HTTP clients connect over the network, and the browser journeys are the only local process (unless the suite owns PostgreSQL). Connection strings come from configuration, and the test-side domain is composed when `ConnectionStrings:Northstar` is set:

```csharp
var composeDomainInTests = hostedInProcess || configuredDatabase is not null || usePostgres;
```

## What doesn't change

- The journeys and their `[ProtoTest]` tests, attributes, authenticators and provisioners.
- The runner attribute and assembly setup: one host per process, same lifecycle.
- The trace and the HTML/JSON reports — every run produces the same artifacts wherever it ran.
- [Skip conditions](../foundation/skip-conditions.md): a test that needs something the host doesn't have skips, so an environment-specific journey is an environment-specific *skip*, not a failure.

## Only an in-process application can do this

Some things need the application's own process; against a published environment they are unreachable, and the suite skips them rather than failing:

| Feature | Why it is in-process only |
| --- | --- |
| `Proto.Context.ApplicationServices<TProgram>()`, `ServerService<TProgram, TService>()`, `ServerFactory<TProgram>()` | they resolve from the in-process server's container; with no server registered, `ApplicationServices` throws |
| `IGraphQLWebSocketFactory` over the test server (the demo registers `NorthstarGraphQLWebSocketFactory` only in-process) | subscriptions ride the in-process WebSocket connection |
| Transactional isolation of the application's writes (`SqlIsolation.Transaction` + `ShareConnectionWith`) | the transaction covers only the connection ProtoTest owns; an application can share it only if it is wired to it, which a deployed process cannot be |

The **test-side domain is not in-process only**: the demo composes it whenever it has a store, which includes a container or a configured connection string. `[RequiresCapability(ProtoCapabilityKinds.Store)]` on `DomainAccessJourney` matches the capability `AddSql` registers, so the journey skips exactly when `composeDomainInTests` is false. The demo sets `SqlIsolation.None` for that domain, because its application has its own connection and a test transaction would hide the test's writes from it.

For a test that only needs *an* in-process server, `[RequiresInProcess]` is the shorthand: it expands to `[RequiresCapability("server")]` and skips whenever no server capability is registered — see [Skip conditions](../foundation/skip-conditions.md) for the exact evaluation and per-runner limits.

## The demo's switches

| Key | Read from | Effect |
| --- | --- | --- |
| `ProtoTest:TargetUrl` | configuration / environment | non-empty selects the published environment and becomes the application's `BaseUrl` |
| `ProtoTest:Database` | configuration / environment | `postgres` starts `PostgresDatabase.Container()` and skips the standalone app |
| `ProtoTest:Messaging:Broker` | configuration / environment | `container` starts `RabbitMqBroker.Container()` |
| `ConnectionStrings:Northstar` | configuration / environment | points the application and the test-side domain at an existing store |
| `ProtoTest:Messaging:RabbitMq:ConnectionString` | configuration / environment | points the messaging adapter at an existing broker |

All of them work as environment variables with the usual `__` separator (`ProtoTest__TargetUrl`). See [Configuration](./configuration.md) for how configuration sources are added and which value wins, and [Infrastructure](../foundation/infrastructure.md) for what the host starts and when it is released.
