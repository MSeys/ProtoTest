---
sidebar_position: 3
title: A write lands in the database
description: Create an order over REST, then read the row the application wrote through Entity Framework Core — against a PostgreSQL container the run owns.
---

import TraceExample from '@site/src/components/TraceExample';

# A write lands in the database

The API answers `201 Created`, but did the order reach the database with the right status? The test creates it over REST, then reads the row itself — on a database the run started, shared with the in-process application.

The same journey runs in the demo — [DomainAccessJourney.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/DomainAccessJourney.cs) writes through REST and reads the committed row through the test-side SQL connection; [Setup.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/Setup.cs) composes both sides over the same store. The full API surface is in [SQL](../integrations/sql/index.md).

<TraceExample
  demo="rest-database"
  title="REST write → committed database row"
  path="POST /api/v1/projects · SELECT Projects"
/>

## Compose

```csharp
protected override void Configure(IProtoHostBuilder builder) =>
    builder
        // One PostgreSQL for the run; the application reads its connection string from this key.
        .AddInfrastructure(PostgresDatabase.Container(), "ConnectionStrings:Orders")
        .AddApplication("Api", app => app
            .AddAspNetCoreServer<Program>()
            .AddRest(rest => rest.AddClient("Api")))
        // The test's own connection, to the same database.
        .AddSql(
            services => new NpgsqlConnection(
                services.GetRequiredService<ProtoInfrastructureSettings>()
                    .Values["ConnectionStrings:Orders"]),
            sql => sql.Isolation = SqlIsolation.None)
        .AddEntityFrameworkCore<OrdersDbContext>((services, options) =>
            options.UseNpgsql(services.GetRequiredService<DbConnection>()));
```

`OrdersDbContext` is the application's own context, so the test reads the row with the same mapping the application wrote it with. Configure it here rather than calling a host `AddDbContext<OrdersDbContext>` afterwards — EF Core keeps the first options registration and drops later ones.

## The test

```csharp
[Application("Api")]
public sealed class OrderPersistenceTests
{
    [ProtoTest]
    public async Task A_created_order_is_stored_as_pending()
    {
        var reference = $"ORD-{Proto.Context.TestId}";

        using var response = await Proto.Context.Rest()
            .Body(new { reference, product = "notebook", quantity = 2 })
            .PostAsync("/api/orders");
        response.Should.HaveHttpStatus(HttpStatusCode.Created);

        var stored = await Proto.Context.Sql<OrdersDbContext>().Orders
            .AsNoTracking()
            .SingleAsync(order => order.Reference == reference);

        Assert.Multiple(() =>
        {
            Assert.That(stored.Status, Is.EqualTo(OrderStatus.Pending));
            Assert.That(stored.Quantity, Is.EqualTo(2));
        });
    }
}
```

## What it proves

The assertion is about the row the application committed — not the request body echoed back. `Proto.Context.Sql<T>()` resolves the scoped context over the test's connection, so the read runs in the same context the trace records next to the REST call.

## Limits

- **Why `SqlIsolation.None`.** The in-process application opens its own connection and commits; a rollback on the test's connection would not undo that write. With the default `Transaction` isolation the run-start guard throws while an application registered through `AddApplication` is not declared with `ShareConnectionWith(...)` — and a declaration is only a statement, not enforcement. See [Isolation](../integrations/sql/index.md#isolation).
- **Unique values, not cleanup.** The container lives for one run, so nothing survives to the next one. Within a run, a reference built from `TestId` keeps parallel tests out of each other's rows.
- **No SQL tracing.** The connection and transaction lifecycle is traced; individual commands are not.
- **Against a deployed environment the suite usually cannot reach the database.** Compose `AddSql` only where it can, and mark the test `[RequiresCapability(ProtoCapabilityKinds.Store)]`: where no store is composed, it skips instead of failing.
