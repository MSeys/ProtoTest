---
sidebar_position: 15
title: Infrastructure recipes
sidebar_label: Recipes
description: "Infrastructure recipes: the sample flows, readiness probes, run-scoped setup, and AddResource against AddInfrastructure."
---

# Infrastructure recipes

The concept is in [Infrastructure](./infrastructure.md): one target, an ordered chain, one winner. The recipes below start from that page's container chain.

## The sample's two flows

The sample suite registers its broker and database targets as above ([Environments](../getting-started/environments.md) shows the three shapes they select). The browser journey's address comes from the application chain.

An application image is the container counterpart of the loopback recipe. `ApplicationContainer` (`ProtoTest.Testcontainers`) starts the image as run infrastructure and fills `ProtoTest:Applications:{application}:BaseUrl` from the mapped address. The application's clients, browser sessions and readiness probe then resolve it:

```csharp
var api = ApplicationContainer.Container("Api", "my-registry.example.test/orders-api:1.4", port: 8080);
builder.AddInfrastructure(
    "OrdersApi",
    chain => chain
        .UseConfigured()
        .UseContainer(api),
    api.BaseUrlKey);
```

Register it instead of `AddAspNetCoreServer` for that application, because a containerized application has no in-process server. [Hosting a browser journey](../integrations/aspnetcore.md#hosting-a-browser-journey) shows the full composition next to the loopback recipe.

## Wait until it is ready

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

- A probe is infrastructure. The host awaits it at its registration position, and it is recorded as a `readiness` run entity carrying the attempts and the wait it spent.
- `ProtoReadiness.Tcp(host, port)` is ready when a connection succeeds. `ProtoReadiness.Http(url)` is ready when the address answers at all. Pass an acceptance check to demand a status or a health payload. Any delegate returning `ValueTask<bool>` works too.
- An exception is "not ready yet". A connection refusal while a container boots is normal, and the last error appears in the timeout failure. The default timeout is 30 seconds.
- One policy governs every wait. `ConfigureReadiness` sets the timeout and interval for host probes **and** for the containers the run starts. The section `ProtoTest:Readiness` binds over the code values when the host is built, so a slow image is tuned in one place.
- A probe that never becomes ready fails the run before the first test, naming the probe, its attempts and the last error.

The shipped containers declare their own checks. A [PostgreSQL container](../integrations/sql/index.md) waits for its standard port to accept connections, and [RabbitMQ](../integrations/messaging/index.md) for the AMQP port. When a custom image listens elsewhere, override the port:

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

Register the probe **after** the piece that publishes the address. Probes are awaited at their registration position. A probe registered first resolves nothing, and instead of waiting it records `readiness.skipped` naming its position and the later publisher. It never claims the application runs in-process. An in-process application has no address to wait for, so the probe is skipped and records why.

## Run-scoped setup

Some run-owned state is an action rather than a piece to own, such as creating the schema of a container database, seeding a catalogue or warming a cache. `AddRunSetup(name, delegate)` runs it once at the run's start, at its registration position in the infrastructure order. A step registered after a container reads the connection string that container published:

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

A step owns nothing to release. Stop and dispose release the run's resources and do not call the step again, and the run records it as an entity like any other piece. A step that throws fails the run's start with its own exception. What had started is released and the host stays retryable, so a retry runs the step again.

Use a step instead of a run hook or a test setup when the state belongs to the whole run. A run hook runs before infrastructure starts and cannot see a container's address. A test hook or test body runs inside the per-test transaction, where its DDL is rolled back with the test. The SQL page shows the [run-owned schema recipe](../integrations/sql/index.md#run-owned-schema).

## `AddResource` versus `AddInfrastructure`

`IProtoHostBuilder.AddResource(IProtoResource)` adds a run-scoped resource and nothing else. The host does **not** call `StartAsync`, since a plain resource has none, and does **not** fill any settings. The resource is still recorded as owned and released with the run.

| | `AddInfrastructure` | `AddResource` |
| --- | --- | --- |
| Starts with the run | yes, the winning provider's piece | no |
| Fills `ProtoInfrastructureSettings` | connection string per key, plus settings | no |
| Registered as run entity and released | yes | yes |

Use `AddInfrastructure` when the piece must start with the run or publish values. Use `AddResource` for an already-started handle that only needs the lifecycle.
