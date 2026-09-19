---
sidebar_position: 3
title: A write lands in the database
description: Create an order over REST, then read the row the application wrote through Entity Framework Core — against a PostgreSQL container the run owns.
---

# A write lands in the database

The API answers `201 Created`, but did the order reach the database with the right status? The test creates it over REST, then reads the row itself — on a database the run started, shared with the in-process application.

## Compose

```csharp
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.Rest;
using ProtoTest.Sql;
using ProtoTest.Sql.EntityFrameworkCore;
using ProtoTest.Sql.Testcontainers;

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
                services.GetRequiredService<ProtoInfrastructureSettings>().Values["ConnectionStrings:Orders"]),
            sql => sql.Isolation = SqlIsolation.None)
        .AddEntityFrameworkCore<OrdersDbContext>((services, options) =>
            options.UseNpgsql(services.GetRequiredService<DbConnection>()));
```

`OrdersDbContext` is the application's own context, so the test reads the row with the same mapping the application wrote it with.

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
        response.ShouldHaveHttpStatus(HttpStatusCode.Created);

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

## Watch for

- **Why `SqlIsolation.None`.** The in-process application opens its own connection and commits. A rollback on the test's connection would not undo that write, so the default `Transaction` isolation would only pretend to clean up. That is why the host refuses `Transaction` until every application is declared as sharing the test's connection. See [Isolation](../integrations/sql/index.md#isolation).
- **Unique values, not cleanup.** The container lives for one run, so nothing survives to the next one. Within a run, a reference built from `TestId` keeps parallel tests out of each other's rows.
- **Read what the application wrote, not what you sent.** Assert on values the application decides — the status, a computed total — rather than echoing the request back.

Against a deployed environment the suite usually cannot reach the database. Compose `AddSql` only where it can, and mark the test `[RequiresCapability(ProtoCapabilityKinds.Store)]`: where no store is composed, it skips instead of failing.
