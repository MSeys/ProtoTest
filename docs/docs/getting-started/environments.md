---
sidebar_position: 4
title: Environments
description: "Run the same suite in-process, container-backed or against a published environment, changing only the host's setup and configuration."
---

# One suite, three environments

The same suite runs in three shapes without a code change: **in-process** on a laptop, **container-backed** on a machine with a container runtime, and **published** against a deployed environment. Only the host's setup and configuration differ — the journeys, attributes, assertions and reports are the same file.

The sample's `NorthstarRun` decides all three from configuration:

```csharp
var run = NorthstarRun.From(configuration);
var hostedInProcess = run.RunsLocalApplications;
var configuredDatabase = run.ConfiguredStore;
var postgresStore = run.UsesPostgres;
var useMessagingContainer = run.OwnsMessagingBroker;
```

| | In-process | Container-backed | Published |
| --- | --- | --- | --- |
| Selected by | `TargetUrl` empty | `ProtoTest:Database=postgres`, `ProtoTest:Messaging:Broker=container` | `TargetUrl` set |
| Application | `AddAspNetCoreServer<Program>` | in-process, fed by infrastructure settings | never started; clients target `BaseUrl` |
| Store | file SQLite under `TestResults/Northstar.ProtoTest` | `PostgresDatabase.Container()` | `ConnectionStrings:Northstar` |
| Broker | none configured; broker journeys skip | `RabbitMqBroker.Container()` | `ProtoTest:Messaging:RabbitMq:ConnectionString` |
| Browser journeys | a loopback listener the run starts | same | the configured `BaseUrl` |

## In-process

With no `TargetUrl`, the host builds the application in-process and the REST, GraphQL and gRPC clients reuse its transport — no port is opened:

```csharp
app.AddAspNetCoreServer<Program>(configureWebHost: webHost =>
{
    if (!postgresStore)
    {
        webHost.UseSetting("ConnectionStrings:Northstar", fallbackDatabase);
    }

    webHost.UseSetting("Database:Provider", databaseProvider);
    webHost.UseSetting("ProtoTest:TestSupport", "true");
});
```

The sample owns a **file** SQLite database per run (`TestResults/Northstar.ProtoTest/northstar.db`, deleted before the run starts) and hosts the application twice over that one store: in-process for the API journeys, and on a loopback listener (`NorthstarProgram.CreateApp`) for the browser journey. The loopback instance is its **own application** (`Northstar web`) whose published base URL the [web sessions](../integrations/web/index.md) follow - one address authority per application.

Because the test-side domain is composed over the same store, `DomainAccessJourney` runs; because no broker is configured, `BrokerJourney` skips.

## Container-backed

Setting `ProtoTest:Database=postgres` or `ProtoTest:Messaging:Broker=container` makes the run own the container through [infrastructure](../foundation/infrastructure.md):

```csharp
builder.AddInfrastructure(
    "MessagingBroker",
    chain => chain
        .UseConfigured()
        .UseContainer(RabbitMqBroker.Container()),
    RabbitMqOptions.ConnectionStringSetting,
    "Messaging:RabbitMq:ConnectionString");

builder.AddInfrastructure(
    "NorthstarDatabase",
    chain => chain
        .UseConfigured()
        .UseContainer(PostgresDatabase.Container()),
    "ConnectionStrings:Northstar");
```

The started connection strings reach the tests through `ProtoInfrastructureSettings` and both hosted instances through their host settings, so all of them work against the same store or broker. The loopback instance follows the suite's configuration, so the web journey runs in every local mode and skips only when the browser is not installed. A published run skips the loopback and follows the configured `ProtoTest:Applications:{application}:BaseUrl` instead.

## Published

Setting `ProtoTest:TargetUrl` points the suite at a deployed system:

```csharp
if (!hostedInProcess)
{
    settings[$"ProtoTest:Applications:{NorthstarTargets.Api}:BaseUrl"] = targetUrl;
}
```

No ASP.NET Core server is registered and no loopback listener is started: HTTP clients connect over the network to `BaseUrl`, and the browser journey follows the same published address when Playwright is available (it skips when the browser is not installed). Connection strings come from configuration, and the test-side domain is composed when `ConnectionStrings:Northstar` is set:

```csharp
var composeDomainInTests = hostedInProcess || configuredDatabase is not null || postgresStore;
```

Because the application is not started by the run, nothing guarantees it is up when the first test runs. `AddHttpReadiness(application)` waits for its published address instead of sleeping in setup, and records the wait in the trace:

```csharp
builder.AddHttpReadiness(NorthstarTargets.Api);   // waits for ProtoTest:Applications:{app}:BaseUrl
```

Register the probe **after** the infrastructure that publishes the address; a probe registered first sees no address and records a `readiness.skipped` reason naming the ordering requirement instead of waiting. An in-process application has no address to wait for, so the probe is skipped there and says why.

## What doesn't change

- The journeys and their `[ProtoTest]` tests, attributes, authenticators and provisioners.
- The runner attribute and assembly setup: one host per process, same lifecycle.
- The trace and the HTML/JSON reports — every run produces the same artifacts wherever it ran.
- Records that outlive the process keep their identity: `Proto.Context.UniqueName("tenant")` derives a deterministic name from the test id, so a rerun against the same configured database or broker never collides; fix `RunPrefix` (`ConfigureTestIds`) when a rerun should reuse the same records.
- [Skip conditions](../foundation/skip-conditions.md): a test that needs something the host doesn't have skips, so an environment-specific journey is an environment-specific *skip*, not a failure.

## Only an in-process application can do this

Some things need the application's own process; against a published environment they are unreachable, and the suite skips them rather than failing:

| Feature | Why it is in-process only |
| --- | --- |
| `Proto.Context.ApplicationServices<TProgram>()`, `ServerService<TProgram, TService>()`, `ServerFactory<TProgram>()` | they resolve from the in-process server's container; with no server registered, `ApplicationServices` throws |
| `IGraphQLWebSocketFactory` over the test server (a suite registers one for in-process subscriptions) | subscriptions ride the in-process WebSocket connection |
| Transactional isolation of the application's writes (`SqlIsolation.Transaction` + `ShareConnectionWith`) | the transaction covers only the connection ProtoTest owns; an application can share it only if it is wired to it, which a deployed process cannot be |

The **test-side domain is not in-process only**: the sample composes it whenever it has a store, which includes a container or a configured connection string. `[RequiresCapability(ProtoCapabilityKinds.Store)]` on `DomainAccessJourney` matches the capability `AddSql` registers, so the journey skips exactly when `composeDomainInTests` is false. The sample sets `SqlIsolation.None` for that domain, because its application has its own connection and a test transaction would hide the test's writes from it.

For a test that only needs *an* in-process server, `[RequiresInProcess]` is the shorthand: it expands to `[RequiresCapability("server")]` and skips whenever no server capability is registered — see [Skip conditions](../foundation/skip-conditions.md) for the exact evaluation and per-runner limits.

## The sample's switches

| Key | Read from | Effect |
| --- | --- | --- |
| `ProtoTest:TargetUrl` | configuration / environment | non-empty selects the published environment and becomes the application's `BaseUrl` |
| `ProtoTest:Database` | configuration / environment | `postgres` starts `PostgresDatabase.Container()` |
| `ProtoTest:Messaging:Broker` | configuration / environment | `container` starts `RabbitMqBroker.Container()` |
| `ConnectionStrings:Northstar` | configuration / environment | points the application and the test-side domain at an existing store |
| `ProtoTest:Messaging:RabbitMq:ConnectionString` | configuration / environment | points the messaging adapter at an existing broker |

All of them work as environment variables with the usual `__` separator (`ProtoTest__TargetUrl`). See [Configuration](./configuration.md) for how configuration sources are added and which value wins, and [Infrastructure](../foundation/infrastructure.md) for what the host starts and when it is released.
