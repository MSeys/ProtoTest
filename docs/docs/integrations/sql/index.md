---
sidebar_position: 9
title: SQL
---

# SQL

`ProtoTest.Sql` gives each test a database connection it owns: opened before the test, optionally wrapped in a transaction that is rolled back when the test ends, and disposed with the test. `ProtoTest.Sql.EntityFrameworkCore` builds Entity Framework Core contexts on that same connection, and `ProtoTest.Sql.Testcontainers` owns a PostgreSQL server for the run. Use it when tests write to a store they control and should leave no trace behind; when the store belongs to a deployed environment you cannot roll back, test through the application's APIs instead.

```bash
dotnet add package ProtoTest.Sql
dotnet add package ProtoTest.Sql.EntityFrameworkCore
dotnet add package ProtoTest.Sql.Testcontainers
```

Only `ProtoTest.Sql` is required. Add the Entity Framework Core adapter when tests use a `DbContext`, and the container package when the run should start its own PostgreSQL.

## Registering

```csharp
builder.AddSql(
    services => new NpgsqlConnection(connectionString),
    sql => sql.Isolation = SqlIsolation.Transaction);
```

`AddSql` takes the factory that creates the test's `DbConnection` and an optional options callback. It registers the `SQL` store capability, the factory and `ProtoSqlSession` as scoped services (so each test gets its own), the options, the test hook that opens and releases the connection, and the run hook that guards the isolation declarations. Calling it twice on one host throws.

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

| Key | Option | Default |
| --- | --- | --- |
| `ProtoTest:Sql:Isolation` | `ProtoSqlOptions.Isolation` (`Transaction` or `None`) | `Transaction` |
| `ProtoTest:Sql:SharedWithApplications` | `ProtoSqlOptions.SharedWithApplications` (a list of application names) | empty |

`SharedWith` is the read-only view of the declared names, `ShareConnectionWith(params string[])` is the code API for the same thing, and `SharesConnectionWith(name)` answers whether one was declared. Configuration is layered over the code callback, as with every [configurable option](../../getting-started/configuration.md).

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

`AddEntityFrameworkCore<TContext>` registers the context scoped and adds an enlistment hook. After the connection hook has opened the connection and begun the transaction, the enlistment hook checks that `dbContext.Database.GetDbConnection()` **is** the connection ProtoTest owns — a context over its own connection throws with an explanation instead of silently escaping the transaction — and then calls `UseTransaction` with the test's transaction. That is why the provider must be configured with `services.GetRequiredService<DbConnection>()`; with `Isolation = None` there is no transaction and the hook does nothing.

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

An application hosted in process receives the same keys as host settings automatically (see [ASP.NET Core](../aspnetcore.md)), so the application and the tests can point at one database without environment variables. The default image is `postgres:16-alpine`, configurable through the builder passed to `Container`. `Container()` does not start anything now: the host starts it with the run, so a missing Docker runtime fails the run's start. `TryStart` reports the reason instead of throwing (for a fallback or a skip decision), and `Start` starts now or throws.

## Tracing

- The connection is a **test-scoped resource**: entity kind `database`, id `database:connection`, described as the connection type and isolation, plus the names it is shared with when there are any. Its release runs in teardown before the test's clients are disposed, and is recorded as a `resource.release` entry with `resource.kind = database`.
- The run registers `SQL` and `Entity Framework Core` as `store` capabilities.
- Individual commands are not traced — ProtoTest records the connection's lifecycle, not the SQL your test sends.

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

## Limits

- **The container needs a container runtime.** `PostgresDatabase.Container()` fails the run at start when the runtime is missing; use `TryStart` outside the host to decide to fall back to another database or skip the tests that need one.
- **The transaction covers one connection.** The application's own connection is not rolled back unless the application is built on ProtoTest's connection; `ShareConnectionWith` declares that fact and satisfies the run-start guard, but it does not make the application use the connection.
- **The guard only sees registered applications.** An application hosted without `AddApplication` cannot be detected, so nothing fails the run if it writes outside the transaction.
- **`AddSql` is once per host.** A second call throws rather than layering a second connection.
