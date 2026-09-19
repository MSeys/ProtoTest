# ProtoTest.Sql.EntityFrameworkCore

Entity Framework Core over the per-test connection, enlisted in its transaction.

```bash
dotnet add package ProtoTest.Sql.EntityFrameworkCore
```

## Quick start

```csharp
builder
    .AddSql(_ => new NpgsqlConnection(connectionString))
    .AddEntityFrameworkCore<OrdersDbContext>((services, options) =>
        options.UseNpgsql(services.GetRequiredService<DbConnection>()));

[ProtoTest]
public async Task Order_ShouldPersistThroughTheContext()
{
    var context = Proto.Context.Sql<OrdersDbContext>();
    context.Orders.Add(new Order { Reference = "ORD-1" });
    await context.SaveChangesAsync();
}
```

## What it adds

- **Registration** — `AddEntityFrameworkCore<TContext>((services, options) => …)` registers the scoped context over the test connection and a `SqlEnlistmentHook<TContext>`, and adds the `Entity Framework Core` capability (`store`).
- **Accessor** — `Proto.Context.Sql<TContext>()` resolves the scoped context from dependency injection.
- **Enlistment** — with transactional isolation the hook calls `UseTransaction(session.Transaction)` and records `sql.enlist`; with `SqlIsolation.None` it only validates the connection.
- **Repeats** — a second registration for the same `TContext` is a no-op; a different context type is its own registration.
- **Capability** — registers `Entity Framework Core` (`store`), so `[RequiresCapability(ProtoCapabilityKinds.Store, CapabilityName = "Entity Framework Core")]` proves composition.
- **Tracing** — `sql.enlist` records the enlistment with `db.context`; individual commands are never traced.

Call order matters: the context must be configured with the test's `DbConnection`, and because EF Core keeps only the first `DbContextOptions<TContext>`, an `AddDbContext` call made after `AddEntityFrameworkCore` has its options dropped. The package has no options type or configuration section of its own; connection and isolation come from `ProtoTest.Sql`.

## Learn more

- [SQL guide](https://prototest.dev/docs/integrations/sql/)
- [SQL isolation tests](https://github.com/MSeys/ProtoTest/blob/main/tests/ProtoTest.Sql.Tests/SqlIsolationTests.cs)
