---
sidebar_position: 9
title: SQL
description: "A database connection each test owns, optionally wrapped in a transaction that is rolled back at the end, with Entity Framework Core on top."
---

# SQL

`ProtoTest.Sql` gives each test a database connection it owns: opened before the test, optionally wrapped in a transaction that is rolled back when the test ends, and disposed with the test. `ProtoTest.Sql.EntityFrameworkCore` builds Entity Framework Core contexts on that same connection, and `ProtoTest.Sql.Testcontainers` owns a PostgreSQL server for the run. Use it when tests write to a store they control and should leave no trace behind; when the store belongs to a deployed environment you cannot roll back, test through the application's APIs instead.

```bash
dotnet add package ProtoTest.Sql --prerelease
dotnet add package ProtoTest.Sql.EntityFrameworkCore --prerelease
dotnet add package ProtoTest.Sql.Testcontainers --prerelease
```

Only `ProtoTest.Sql` is required. Add the Entity Framework Core adapter when tests use a `DbContext`, and the container package when the run should start its own PostgreSQL. ProtoTest targets .NET 8, 9 and 10; the template defaults to `net10.0` unless `-f` is passed.

## Registering

```csharp
builder.AddSql(
    services => new NpgsqlConnection(connectionString),
    sql => sql.Isolation = SqlIsolation.Transaction);
```

`AddSql` takes the factory that creates the test's `DbConnection` and an optional options callback:

```csharp
public static IProtoHostBuilder AddSql(
    this IProtoHostBuilder builder,
    Func<IServiceProvider, DbConnection> connectionFactory,
    Action<SqlOptions>? configure = null);
```

It registers the `SQL` store capability, the factory and `ProtoSqlSession` as scoped services (so each test gets its own), the options, the test hook that opens and releases the connection, and the run hook that guards the isolation declarations. Calling it twice on one host is a no-op: the first registration's factory and options win. The options singleton is built by running `configure` and is then bound from `ProtoTest:Sql`, so configuration layers over code.

The factory runs inside the test's scope, so it can resolve services — the demo reads a container's connection string from infrastructure settings:

```csharp
provider => CreateDatabaseConnection(ResolveDatabase(provider, fallbackDatabase), usePostgres)
```

## Isolation

`SqlIsolation` has two modes:

| Mode | What happens |
| --- | --- |
| `Transaction` (default) | The connection opens and a transaction begins before the test body. When the test ends the transaction is rolled back and the connection disposed, so every write made through that connection disappears. |
| `None` | No transaction. Writes persist, and provisioners are responsible for releasing what they created. |

`Transaction` promises exactly one thing: **writes made through the connection ProtoTest owns are rolled back.** Entity Framework Core, Dapper and raw ADO.NET all count, because they use that connection. A write through a different connection — an application that opened its own, a second connection created by a helper — is not covered and is committed when that connection commits.

Because that promise is easy to believe wrongly, the host registers a guard. When the host has applications registered through `AddApplication` and isolation is `Transaction`, every one of them must be declared as using the test's connection:

```csharp
builder.AddSql(
    services => new NpgsqlConnection(connectionString),
    sql => sql.ShareConnectionWith("Orders", "Billing"));
```

An undeclared application fails the run at start with an explanation telling you to declare it or to use `SqlIsolation.None`. Applications that were not registered through `AddApplication` are invisible to the guard, and a declaration is a statement that the application uses the connection — if it actually opens its own, its writes still commit.

## Options and configuration

| Key | Bound to | Type | Default |
| --- | --- | --- | --- |
| `ProtoTest:Sql:Isolation` | `SqlOptions.Isolation` | `SqlIsolation`: `Transaction` or `None` | `Transaction` |
| `ProtoTest:Sql:SharedWithApplications` | `SqlOptions.SharedWithApplications` | list of application names (bound as an array, for example `ProtoTest:Sql:SharedWithApplications:0`) | empty |

`SharedWith` is the read-only, case-insensitive set of the declared names; `ShareConnectionWith(params string[])` is the code API for adding them (it validates that each name is non-empty), and `SharesConnectionWith(name)` answers membership. The binding setter clears the set first and skips null or whitespace entries. Configuration is layered over the code callback, as with every [configurable option](../../getting-started/configuration.md).

## Reading the connection in a test

```csharp
DbConnection connection = Proto.Context.SqlConnection();
ProtoSqlSession session = Proto.Context.SqlSession();
DbTransaction? transaction = Proto.Context.SqlTransaction();   // null with SqlIsolation.None
```

`ProtoSqlSession` exposes the owned `Connection` and, when isolation is `Transaction`, the `Transaction` every access technology enlists in.

## Entity Framework Core

```csharp
builder
    .AddSql(services => new NpgsqlConnection(connectionString))
    .AddEntityFrameworkCore<OrdersDbContext>((services, options) =>
        options.UseNpgsql(services.GetRequiredService<DbConnection>()));

// in a test
var context = Proto.Context.Sql<OrdersDbContext>();
context.Orders.Add(new Order { Reference = "ORD-1" });
await context.SaveChangesAsync();
```

`AddEntityFrameworkCore<TContext>` registers the context scoped and adds an enlistment hook:

```csharp
public static IProtoHostBuilder AddEntityFrameworkCore<TContext>(
    this IProtoHostBuilder builder,
    Action<IServiceProvider, DbContextOptionsBuilder> configure) where TContext : DbContext;
```

After the connection hook has opened the connection and begun the transaction, the enlistment hook checks that `dbContext.Database.GetDbConnection()` **is** the connection ProtoTest owns — a context over its own connection throws with an explanation instead of silently escaping the transaction — and then records `sql.enlist` and calls `UseTransaction` with the test's transaction. That is why the provider must be configured with `services.GetRequiredService<DbConnection>()`; with `Isolation = None` there is no transaction and no enlistment.

**Call order matters.** Entity Framework Core keeps the first `DbContextOptions<TContext>` registration and drops later options delegates. `AddEntityFrameworkCore` therefore registers the context only when no `DbContextOptions<TContext>` exists yet: a host `AddDbContext` called **before** it keeps its own configuration and is still enlisted, while one called **after** has its options dropped. Register the host's context first when it needs both its options and the test transaction. A repeated `AddEntityFrameworkCore<TContext>` is a no-op (a ProtoTest-owned marker gates it); two different contexts are different registrations and both apply.

## PostgreSQL container

```csharp
builder.AddInfrastructure(PostgresDatabase.Container(), "ConnectionStrings:Northstar");
```

[Infrastructure](../../foundation/infrastructure.md) is started by the host before the run and released after it stops; the container's started connection string fills every key you list. Tests read the value from `ProtoInfrastructureSettings`:

```csharp
var connectionString = provider.GetService<ProtoInfrastructureSettings>() is { } settings
    && settings.Values.TryGetValue("ConnectionStrings:Northstar", out var connection)
    ? connection
    : fallback;
```

An application hosted in process receives the same keys as host settings automatically (see [ASP.NET Core](../aspnetcore.md)), so the application and the tests can point at one database without environment variables. The default image is `postgres:16-alpine`, configurable through the builder passed to `Container`. `Container()` does not start anything now: the host starts it with the run — before any test-level skip condition — so a missing Docker runtime fails the run's start. `TryStart` reports the reason instead of throwing: call it in the suite fixture before `AddInfrastructure` to fall back or skip the suite, and `Start` starts now or throws.

## Tracing

- Operations follow the connection's lifecycle, all with source `ProtoTest.Sql`: `sql.connection.open` (Setup, with `sql.connection.type`), `sql.transaction.begin` (Setup, child of the open, with `sql.isolation`), `sql.transaction.rollback` (release phase, inside the connection resource's release), and `sql.enlist` (Setup, when a `DbContext` joins the transaction, with `db.context`, source `ProtoTest.Sql.EntityFrameworkCore`).
- The connection is a **test-scoped resource**: entity kind `database`, id `database:connection`, described as the connection type and isolation, plus the names it is shared with when there are any. Its release runs in teardown before the test's clients are disposed, and is recorded as a `resource.release` entry with `resource.kind = database`.
- The run registers `SQL` and `Entity Framework Core` as `store` capabilities.
- Individual commands are not traced, and neither package emits observations or report items — ProtoTest records the connection's lifecycle, not the SQL your test sends.

## The demo's wiring

The sample suite composes its own domain over the connection ProtoTest owns, except when it is testing a published environment. It owns a PostgreSQL container when `ProtoTest:Database=postgres`, and hands the started connection string to both sides:

```csharp
if (usePostgres)
{
    builder.AddInfrastructure(PostgresDatabase.Container(), "ConnectionStrings:Northstar");
}

builder
    .AddSql(
        provider => CreateDatabaseConnection(ResolveDatabase(provider, fallbackDatabase), usePostgres),
        sql => sql.Isolation = SqlIsolation.None)
    .ConfigureServices(services => services.AddNorthstarDomain(
        (provider, options) =>
        {
            var connection = provider.GetRequiredService<DbConnection>();
            if (usePostgres) options.UseNpgsql(connection);
            else options.UseSqlite(connection);
        },
        ServiceLifetime.Scoped));
```

Isolation stays `None` on purpose: the in-process application keeps its own connection, so a test transaction would hide the test's writes from it. The demo releases what it creates instead. When PostgreSQL is not used, the suite owns a file database and the application is given that connection string through its host settings.

## Skip

The capabilities are name `"SQL"` kind `store` and name `"Entity Framework Core"` kind `store`. Skip with:

```csharp
[RequiresCapability(ProtoCapabilityKinds.Store)]
[RequiresCapability("store", CapabilityName = "SQL")]
```

There are no package-specific attributes. See [Skip conditions](../../foundation/skip-conditions.md).

## Limits

- **The container needs a container runtime.** The container starts with the host, before any test-level skip condition, so `PostgresDatabase.Container()` fails the run at start when the runtime is missing; call `TryStart` in the suite fixture before registering it to fall back to another database, or skip the suite with the reported reason.
- **The transaction covers one connection.** The application's own connection is not rolled back unless the application is built on ProtoTest's connection; `ShareConnectionWith` declares that fact and satisfies the run-start guard, but it does not make the application use the connection.
- **The guard only sees registered applications.** An application hosted without `AddApplication` cannot be detected, so nothing fails the run if it writes outside the transaction.
- **`AddSql` is once per host.** A second call is a no-op rather than layering a second connection: the first registration's factory and options win, matching [repeated registration](../../getting-started/configuration.md#repeated-registration).
- **A rollback failure still disposes everything.** The transaction and the connection are disposed in their own `finally` blocks even when rollback throws; the release failure is aggregated like any other teardown failure.
- **The test owns the schema.** There is no automatic migration or database creation: the factory returns the connection and the test (or its fixture) sets the schema up.

## Links

- [Integrations overview](../overview.md) — where the store packages sit.
- [One suite, three environments](../../getting-started/environments.md) — the demo's SQLite and PostgreSQL switch.
- [Infrastructure](../../foundation/infrastructure.md) — how `PostgresDatabase.Container()` starts and fills settings.
- EF Core registration order and enlistment in [`tests/ProtoTest.Sql.Tests/SqlIsolationTests.cs`](https://github.com/MSeys/ProtoTest/blob/main/tests/ProtoTest.Sql.Tests/SqlIsolationTests.cs), and the demo's composition in [`samples/ProtoTest.Demo/Setup.cs`](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/Setup.cs).
