# ProtoTest.Sql.EntityFrameworkCore

Entity Framework Core access over the [ProtoTest.Sql](../ProtoTest.Sql) per-test connection.

```bash
dotnet add package ProtoTest.Sql.EntityFrameworkCore --prerelease
```

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

The context is built on the connection ProtoTest owns, so `Transaction` rolls back Entity Framework Core writes and raw commands alike, and the connection is released as an owned resource when the test ends.
