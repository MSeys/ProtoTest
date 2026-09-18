# ProtoTest.Sql

One database connection per test, opened and owned as a resource, with per-test isolation.

```bash
dotnet add package ProtoTest.Sql --prerelease
```

```csharp
builder.AddSql(
    _ => new NpgsqlConnection(connectionString),
    sql => sql.ShareConnectionWith("Orders"));
```

```csharp
[ProtoTest]
[Application("Orders")]
public async Task Order_ShouldBeVisibleToTheStore()
{
    await using var command = Proto.Context.SqlConnection().CreateCommand();
    command.CommandText = "insert into orders (reference) values ('ORD-1')";
    await command.ExecuteNonQueryAsync();
}
```

With `Transaction` (the default) everything written through that connection is rolled back when the test ends. Access technologies share the same connection, so Entity Framework Core, Dapper and raw ADO.NET are covered by one transaction - see [ProtoTest.Sql.EntityFrameworkCore](../ProtoTest.Sql.EntityFrameworkCore).

The transaction covers that connection only. When the host registers applications, each one must be declared with `ShareConnectionWith(...)` to state that it uses the connection; otherwise the run fails at start with an explanation. Tests that own the connection alone need no declaration.
