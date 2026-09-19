# ProtoTest.Sql.Testcontainers

A PostgreSQL container owned by the whole run, for suites that want a real server instead of an in-process database.

```bash
dotnet add package ProtoTest.Sql.Testcontainers
```

## Quick start

```csharp
var database = PostgresDatabase.Container(container => container.WithImage("postgres:17-alpine"));
builder.AddInfrastructure(database, "ConnectionStrings:Northstar");

// The host starts the container and fills the key for the application under test;
// the resource itself exposes the same started connection string.
services.AddNorthstarDomain(options => options.UseNpgsql(database.ConnectionString));
```

## What it adds

- **Container resource** — `PostgresDatabase` derives `ProtoContainerResource<PostgreSqlContainer>` with `Id "database:postgres"`, `Kind "database"` and `Description "PostgreSQL container"`; the builder starts from the `postgres:16-alpine` image.
- **Factories** — `Container(configure?)` builds without starting, `Start(configure?)` starts now or throws with the reason, and `TryStart(configure, out database, out error)` reports failure instead of throwing.
- **Registration** — `AddInfrastructure(database, "ConnectionStrings:Northstar")` starts it with the host and fills the key; `AddResource` alone only owns its release.
- **Lifecycle** — start-once per run with a shared start task; released when the host is disposed, after the run stopped and the reports were written.
- **Surface** — inherited `ConnectionString` (empty until started), `IsStarted`, `StartAsync`, `ReleaseAsync` and `DisposeAsync`.
- **Capability** — none of its own: `AddSql` adds the `SQL` (`store`) capability, and fixture-level `TryStart` is the fallback when the runtime is missing.
- **Dependencies** — depends only on `ProtoTest.Testcontainers` plus the Testcontainers PostgreSQL module, so the container API stays out of Core.
- **Alternative** — a connection string for an external PostgreSQL server is ordinary configuration; this package is only for the suite that wants to own one.

## Configuration

No options type of its own: the image and container settings come from the `PostgreSqlBuilder` passed to `Container`/`Start`/`TryStart`, and the connection string flows through the keys passed to `AddInfrastructure` — caller-supplied strings, not a bound section.

The container starts with the host — before any test-level skip condition — so a missing container runtime fails the run at start. Use `TryStart` in the suite fixture before registering to choose another mode or skip the suite, and note there is no per-test database reset.

## Learn more

- [SQL guide](https://prototest.dev/docs/integrations/sql/)
- [Demo container registration](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/Setup.cs)
