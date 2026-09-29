---
sidebar_position: 9
title: SQL
description: "A database connection each test owns, optionally wrapped in a transaction that is rolled back at the end, with Entity Framework Core on top."
---

# SQL

## What it adds

`ProtoTest.Sql` gives each test a database connection it owns: opened before the test, optionally wrapped in a transaction that is rolled back when the test ends, and disposed with the test. `ProtoTest.Sql.EntityFrameworkCore` builds Entity Framework Core contexts on that same connection, and `ProtoTest.Sql.Testcontainers` owns a PostgreSQL server for the run. Use it when tests write to a store they control and must leave no rows behind. When the store belongs to a deployed environment, test through the application APIs instead.

Writes made through the connection ProtoTest owns disappear at teardown:

| Write path | `Transaction` (default) | `None` |
| --- | --- | --- |
| Through the owned connection | rolled back | persists; provisioners release it |
| Through the application's own connection | commits | commits |

See [Isolation](#isolation) for the promise and its guard.

## Stores other than SQL

This page is relational: `ProtoTest.Sql` owns a `DbConnection` per test. When the store is MongoDB, Redis, Elasticsearch or anything else without ADO.NET, pick one of three options:

1. **Test through the application APIs.** When the store belongs to a deployed environment rather than the test, drive it through the application's REST, GraphQL or gRPC surface and assert on what comes back. Nothing here is needed.
2. **Register a raw client.** When the test must reach the store directly, register the client as an ordinary scoped service on the host builder and resolve it in the test. There is no per-test transaction or rollback; provision what the test needs and release it in teardown or with a [provisioner](../data/provisioners.md).
3. **Write an adapter that follows the SQL rule.** When several suites need the same owned-connection shape, package it like `ProtoTest.Sql` does: a host builder extension that registers the client scoped, a test hook that opens and releases it, and the same honest capability rule. `SqlAddressRule.DeclaredKeys` and `SqlAddressRule.IsInert` carry the address decision, and `AddEntityFrameworkCore` is the canonical example to copy (see [For package authors](#for-package-authors)).

## Install

```bash
dotnet add package ProtoTest.Sql
dotnet add package ProtoTest.Sql.EntityFrameworkCore
dotnet add package ProtoTest.Sql.Testcontainers
```

Only `ProtoTest.Sql` is required. Add the Entity Framework Core adapter when tests use a `DbContext`, and the container package when the run should start its own PostgreSQL. ProtoTest targets .NET 8, 9 and 10; the template defaults to `net10.0` unless `-f` is passed.

The snippets assume the namespaces of the types they name: `ProtoTest.Sql`, `ProtoTest.Sql.EntityFrameworkCore`, `System.Data.Common`, your provider (`Npgsql`), and your runner's attribute namespace for `[ProtoTest]` (`ProtoTest.NUnit` for NUnit, listed with the other runners in [runners](../../runners/overview.md)). A missing `ProtoTest.NUnit` turns `[ProtoTest]` into CS0616, not into a skipped test.

## Compose

```csharp
using System.Data.Common;
using Npgsql;
using ProtoTest.Sql;

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

It registers the `SQL` store capability, the factory and `ProtoSqlSession` as scoped services (so each test gets its own), the options, the test hook that opens and releases the connection, and the run hook that guards the isolation declarations. Calling it twice on one host changes nothing. The first registration keeps its factory and options. A host that registered its own `SqlOptions` keeps them: the options registration is `TryAdd`, so the rest of the integration composes around the host's instance instead of switching itself off. The options singleton is built by running `configure` and is then bound from `ProtoTest:Sql`, so configuration layers over code.

The factory runs inside the test's scope, so it can resolve services. The demo reads a container's connection string from infrastructure settings:

```csharp
provider => CreateDatabaseConnection(ResolveDatabase(provider, fallbackDatabase), usePostgres)
```

When the address is environment-dependent, declare the keys that can provide the connection:

```csharp
builder.AddSql(
    provider => new NpgsqlConnection(...),
    sql => sql.AddressKeys.Add("ConnectionStrings:Orders"));
```

With at least one key declared, `AddSql` declares the `SQL` store capability only while one of them can provide a connection: a configured value, or a key a registered container declares and fills. When none can, the integration is inert: the connection is not opened during setup, and `Proto.Context.Sql()`, `SqlConnection()` and `SqlTransaction()` throw naming the missing keys and the `[RequiresCapability(ProtoCapabilityKinds.Store)]` gate. `AddressKeys` is a code-only API: it is filled with `Add` (or a `SqlOptions` instance registered before `AddSql`), because the capability decision is made when the host is built.

### Isolation

`SqlIsolation` has two modes:

| Mode | What happens |
| --- | --- |
| `Transaction` (default) | The connection opens and a transaction begins before the test body. When the test ends the transaction is rolled back and the connection disposed, so every write made through that connection disappears. |
| `None` | No transaction. Writes persist, and provisioners are responsible for releasing what they created. |

`Transaction` promises exactly one thing: **writes made through the connection ProtoTest owns are rolled back.** Entity Framework Core, Dapper and raw ADO.NET all count, because they use that connection. A write through a different connection, such as an application that opened its own or a second connection created by a helper, is not covered and is committed when that connection commits.

| Write path | Transaction | None |
| --- | --- | --- |
| Through the owned connection | rolled back | persists; provisioners release it |
| Through the application's own connection | commits | commits |
| Through a second helper connection | commits | commits |

Because that promise is easy to believe wrongly, the host registers a guard. When the host has applications registered through `AddApplication` and isolation is `Transaction`, every one of them must be declared as using the test's connection:

```csharp
builder.AddSql(
    services => new NpgsqlConnection(connectionString),
    sql => sql.ShareConnectionWith("Orders", "Billing"));
```

An undeclared application fails the run at start with an explanation telling you to declare it or to use `SqlIsolation.None`. Applications that were not registered through `AddApplication` are invisible to the guard, and a declaration is a statement that the application uses the connection; if it actually opens its own, its writes still commit.

### Options and configuration

| Key | Bound to | Type | Default |
| --- | --- | --- | --- |
| `ProtoTest:Sql:Isolation` | `SqlOptions.Isolation` | `SqlIsolation`: `Transaction` or `None` | `Transaction` |
| `ProtoTest:Sql:SharedWithApplications` | `SqlOptions.SharedWithApplications` | list of application names (bound as an array, for example `ProtoTest:Sql:SharedWithApplications:0`) | empty |

`SharedWith` is the read-only, case-insensitive set of the declared names; `ShareConnectionWith(params string[])` is the code API for adding them (it validates that each name is non-empty), and `SharesConnectionWith(name)` answers membership. The binding setter clears the set first and skips null or whitespace entries. Configuration is layered over the code callback, as with every [configurable option](../../getting-started/configuration.md).

### Reading the connection in a test

```csharp
DbConnection connection = Proto.Context.SqlConnection();
ProtoSqlSession session = Proto.Context.Sql();
DbTransaction? transaction = Proto.Context.SqlTransaction();   // null with SqlIsolation.None
```

`ProtoSqlSession` exposes the owned `Connection` and, when isolation is `Transaction`, the `Transaction` every access technology enlists in.

The provider's own `ConnectionString` on the opened connection is the post-open form and can drop credentials: Npgsql removes the password from it once the connection is open. Read the connection string the run started, password included, from `ProtoInfrastructureSettings.Values` (as above) when you need to hand it to something else; read `Proto.Context.SqlConnection().ConnectionString` only when the opened form is what you want.

## The tasks

### Entity Framework Core

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

After the connection hook has opened the connection and begun the transaction, the enlistment hook checks that `dbContext.Database.GetDbConnection()` **is** the connection ProtoTest owns (a context over its own connection throws with an explanation instead of silently escaping the transaction), and then records `sql.enlist` and calls `UseTransaction` with the test's transaction. That is why the provider must be configured with `services.GetRequiredService<DbConnection>()`; with `Isolation = None` there is no transaction and no enlistment.

`AddEntityFrameworkCore` shares the SQL address rule: when `AddSql` declared `AddressKeys` and none is provided, the `Entity Framework Core` store capability is absent too, gated tests skip, the enlistment hook leaves the session and the context alone, and `Proto.Context.Sql<TContext>()` throws the same missing-keys message. The keys are read when `AddEntityFrameworkCore` runs, so call it after `AddSql`; without declared keys the capability stays unconditional.

**Call order matters.** Entity Framework Core keeps the first `DbContextOptions<TContext>` registration and drops later options delegates. `AddEntityFrameworkCore` therefore registers the context only when no `DbContextOptions<TContext>` exists yet: a host `AddDbContext` called **before** it keeps its own configuration and is still enlisted, while one called **after** has its options dropped. Register the host's context first when it needs both its options and the test transaction. A repeated `AddEntityFrameworkCore<TContext>` is a no-op (a ProtoTest-owned marker gates it); two different contexts are different registrations and both apply.

### PostgreSQL container

```csharp
builder.AddInfrastructure(
    "NorthstarDatabase",
    chain => chain.UseContainer(PostgresDatabase.Container()),
    "ConnectionStrings:Northstar");
```

[Infrastructure](../../foundation/infrastructure.md) is started by the host before the run and released after it stops; the container's started connection string fills every key you list. Tests read the value from `ProtoInfrastructureSettings`:

```csharp
var connectionString = provider.GetService<ProtoInfrastructureSettings>() is { } settings
    && settings.Values.TryGetValue("ConnectionStrings:Northstar", out var connection)
    ? connection
    : fallback;
```

An application hosted in process receives the same keys as host settings automatically (see [ASP.NET Core](../aspnetcore.md)), so the application and the tests can point at one database without environment variables. The default image is `postgres:16-alpine`, configurable through the builder passed to `Container`. `Container()` does not start anything now: the host starts it with the run, before any test-level skip condition, so a missing Docker runtime fails the run's start. `TryStart` reports the reason instead of throwing: call it in the suite fixture before `AddInfrastructure` to fall back or skip the suite, and `Start` starts now or throws.

### Run-owned schema

A container database starts empty, and the schema must exist before the first test, but a test body and a test hook run inside the per-test transaction, so `EnsureCreated`/`Migrate` or raw DDL there is rolled back with the test. Create it once for the run with [run-scoped setup](../../foundation/infrastructure-recipes.md#run-scoped-setup), registered **after** the container so it reads the connection string the container published:

```csharp
builder
    .AddInfrastructure(
        "OrdersDatabase",
        chain => chain.UseContainer(PostgresDatabase.Container()),
        "ConnectionStrings:Orders")
    .AddSql(
        provider => new NpgsqlConnection(ResolveDatabase(provider, "ConnectionStrings:Orders")),
        sql => sql.AddressKeys.Add("ConnectionStrings:Orders"))
    .AddEntityFrameworkCore<OrdersDbContext>((services, options) =>
        options.UseNpgsql(services.GetRequiredService<DbConnection>()))
    .AddRunSetup("database schema", async setup =>
    {
        var connectionString = setup.Settings.Values.TryGetValue("ConnectionStrings:Orders", out var published)
            ? published
            : setup.Configuration["ConnectionStrings:Orders"]
              ?? throw new InvalidOperationException(
                  "Register the PostgreSQL container or configure 'ConnectionStrings:Orders'.");
        var options = new DbContextOptionsBuilder<OrdersDbContext>().UseNpgsql(connectionString).Options;
        await using var context = new OrdersDbContext(options);
        await context.Database.EnsureCreatedAsync(setup.CancellationToken);
    });
```

The context here is built over its own connection on purpose: the step runs outside every test, before the per-test transactions begin. `EnsureCreatedAsync` creates the schema of a model without migrations; a suite that ships migrations calls `MigrateAsync` instead. `ResolveDatabase` is the demo's published-first, configured-second read ([below](#the-demos-wiring)). `Npgsql.EntityFrameworkCore.PostgreSQL` is the application's provider package, pinned to the Entity Framework Core version `ProtoTest.Sql.EntityFrameworkCore` brings.

The schema then survives the rollback; the rows do not. Every test sees the tables and writes through `Proto.Context.Sql<TContext>()`, and its transaction carries the writes away when it ends.

## In the trace and coverage

```text
sql.connection.open · NpgsqlConnection (Setup)
├─ sql.transaction.begin (child, sql.isolation = Transaction)
├─ sql.enlist · OrdersDbContext (Setup, source ProtoTest.Sql.EntityFrameworkCore)
└─ sql.transaction.rollback (release phase, inside the connection resource release)
```

- Operations follow the connection's lifecycle, all with source `ProtoTest.Sql`: `sql.connection.open` (Setup, with `sql.connection.type`), `sql.transaction.begin` (Setup, child of the open, with `sql.isolation`), `sql.transaction.rollback` (release phase, inside the connection resource's release), and `sql.enlist` (Setup, when a `DbContext` joins the transaction, with `db.context`, source `ProtoTest.Sql.EntityFrameworkCore`).
- The connection is a **test-scoped resource**: entity kind `database`, id `database:connection`, described as the connection type and isolation, plus the names it is shared with when there are any. Its release runs in teardown before the test's clients are disposed, and is recorded as a `resource.release` entry with `resource.kind = database`.
- The run registers `SQL` and `Entity Framework Core` as `store` capabilities. Both are declared only while the SQL address keys can provide a connection; with declared-but-unprovided keys the trace records both as `capability.skipped` and no connection is opened.
- Individual commands are not traced, and neither package emits observations or report items: ProtoTest records the connection's lifecycle, not the SQL your test sends.

## The demo's wiring

The sample suite composes its own domain over the connection ProtoTest owns, with `SqlIsolation.None` because the in-process application keeps its own connection. See the composition in [`samples/Northstar.ProtoTest/Setup.cs`](https://github.com/MSeys/ProtoTest/blob/main/samples/Northstar.ProtoTest/Setup.cs).

## Skip

The capabilities are name `"SQL"` kind `store` and name `"Entity Framework Core"` kind `store`. Skip with:

```csharp
[RequiresCapability(ProtoCapabilityKinds.Store)]
[RequiresCapability("store", CapabilityName = "SQL")]
```

With `AddressKeys` declared and none of them provided, the `SQL` capability is absent, so the first gate skips instead of failing setup. The `Entity Framework Core` capability follows the same keys, so it drops with `SQL`. There are no package-specific attributes. See [Skip conditions](../../foundation/skip-conditions.md).

## Limits

| Limit | Matters when | Severity |
| --- | --- | --- |
| **The container needs a container runtime.** The container starts with the host, before any test-level skip condition, so `PostgresDatabase.Container()` fails the run at start when the runtime is missing; call `TryStart` in the suite fixture before registering it to fall back to another database, or skip the suite with the reported reason. | The run owns its database but no runtime is installed. | The run fails at start. |
| **The transaction covers one connection.** The application's own connection is not rolled back unless the application is built on ProtoTest's connection; `ShareConnectionWith` declares that fact and satisfies the run-start guard, but it does not make the application use the connection. | The application opens its own connection. | Its writes commit and stay behind. |
| **The guard only sees registered applications.** An application hosted without `AddApplication` cannot be detected, so nothing fails the run if it writes outside the transaction. | An application is hosted outside `AddApplication`. | Writes outside the transaction commit silently. |
| **`AddSql` is once per host.** A second call is a no-op rather than layering a second connection: the first registration's factory and options win, matching [repeated registration](../../getting-started/configuration.md#repeated-registration). | `AddSql` is called twice on one host. | The second call is ignored. |
| **A rollback failure still disposes everything.** The transaction and the connection are disposed in their own `finally` blocks even when rollback throws; the release failure is aggregated like any other teardown failure. | Rollback throws. | The failure surfaces as a teardown failure; nothing leaks. |
| **The connect timeout is provider-owned.** The connection open and transaction begin observe `ProtoExecutionContext.CancellationToken`, or the runner's own token where its adapter has one (table below). Without a token the provider's own connect timeout ends the wait (Npgsql's default, or `Connect Timeout` in the connection string). | Opening the connection hangs. | The provider timeout ends the wait. |
| **No automatic migration or database creation.** ProtoTest never creates or migrates a schema by itself. When the run owns the database, create the schema once with [`AddRunSetup`](#run-owned-schema): a test body or hook runs inside the rolled-back transaction, so `EnsureCreated`/`Migrate` there disappears with the test. A deployed environment keeps its own schema. | The run owns an empty database. | Tests fail on the missing schema until `AddRunSetup` creates it. |

The cancellation source per runner:

| Runner | Cancellation source |
| --- | --- |
| NUnit | the test context token via `[CancelAfter]` |
| xUnit v2 | the runner's `CancellationTokenSource` |
| xUnit v3 | `TestContext.Current.CancellationToken` |
| TUnit | `TestContext.CancellationToken` |
| MSTest | none (4.0.2 floor exposes none) |

## For package authors

A sibling access technology that wants the same honest capability shares the rule instead of re-deriving it: `SqlAddressRule.DeclaredKeys(services)` returns the keys the first `AddSql` recorded, so a package registers its own store capability with `AddCapabilityWhenProvided` over them, and `SqlAddressRule.IsInert(context, options)` (or `ThrowIfInert`) applies the same decision at use time. `AddEntityFrameworkCore` is the shipped example; a Dapper or raw ADO.NET package follows the same shape.

## Links

- [Integrations map](../overview.md) - where the store packages sit.
- [One suite, three environments](../../getting-started/environments.md) - the sample suite's SQLite and PostgreSQL switch.
- [Infrastructure](../../foundation/infrastructure.md) - how `PostgresDatabase.Container()` starts and fills settings.
- EF Core registration order and enlistment in [`tests/ProtoTest.Sql.Tests/SqlIsolationTests.cs`](https://github.com/MSeys/ProtoTest/blob/main/tests/ProtoTest.Sql.Tests/SqlIsolationTests.cs), and the sample suite's composition in [`samples/Northstar.ProtoTest/Setup.cs`](https://github.com/MSeys/ProtoTest/blob/main/samples/Northstar.ProtoTest/Setup.cs).
