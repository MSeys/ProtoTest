# ProtoTest.Sql.Testcontainers

A database container owned by the whole run, for suites that want a real server instead of an in-process database.

```bash
dotnet add package ProtoTest.Sql.Testcontainers --prerelease
```

```csharp
var database = PostgresDatabase.Start(container => container.WithImage("postgres:17-alpine"));
builder.AddResource(database);

// hand the same connection string to the application under test and to the tests
services.AddNorthstarDomain(options => options.UseNpgsql(database.ConnectionString));
```

`PostgresDatabase` is a run-scoped resource: it starts once for the suite and is released when the host is disposed, after the run has stopped and the reports are written. `TryStart` reports why a container could not start instead of throwing, so a machine without a container runtime can fall back to another database or skip the tests that need one.
