# ProtoTest.Sql

One database connection per test, opened and owned as a resource, with optional per-test transaction isolation.

```bash
dotnet add package ProtoTest.Sql
```

## Quick start

```csharp
builder.AddSql(
    _ => new NpgsqlConnection(connectionString),
    sql => sql.ShareConnectionWith("Orders"));

[ProtoTest]
[Application("Orders")]
public async Task Order_ShouldBeVisibleToTheStore()
{
    await using var command = Proto.Context.SqlConnection().CreateCommand();
    command.CommandText = "insert into orders (reference) values ('ORD-1')";
    await command.ExecuteNonQueryAsync();
}
```

## What it adds

- **Connection** — `AddSql(Func<IServiceProvider, DbConnection>, configure?)` registers a scoped connection and session; repeats are no-ops and the first factory wins.
- **Accessors** — `Proto.Context.SqlConnection()`, `SqlSession()` and `SqlTransaction()` (null when isolation is `None`).
- **Isolation** — `SqlIsolation.Transaction` (default) opens a transaction that is rolled back at test end, so writes through that connection never persist; `SqlIsolation.None` persists them and lets provisioners clean up.
- **Sharing guard** — with transactional isolation every application not declared through `ShareConnectionWith(...)` fails the run at `BeforeRunAsync` with guidance.
- **Tracing** — `sql.connection.open`, `sql.transaction.begin` and `sql.transaction.rollback`, plus the connection as a `database` resource.

## Configuration

| Key | Type | Default |
| --- | --- | --- |
| `ProtoTest:Sql:Isolation` | `SqlIsolation` (`Transaction`/`None`) | `Transaction` |
| `ProtoTest:Sql:SharedWithApplications` | `List<string>` | empty |

The transaction covers only the connection ProtoTest owns; `ShareConnectionWith` is a declaration, not enforcement, and the guard sees only applications registered with `AddApplication`. Commands are never traced.

## Learn more

- [SQL guide](https://prototest.dev/docs/integrations/sql/)
- [Demo SQL registration](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/Setup.cs)
