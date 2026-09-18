# ProtoTest.Sql.Testcontainers

A database container owned by the whole run, for suites that want a real server instead of an in-process database.

```bash
dotnet add package ProtoTest.Sql.Testcontainers --prerelease
```

```csharp
var database = PostgresDatabase.Container(container => container.WithImage("postgres:17-alpine"));
builder.AddInfrastructure(database, "ConnectionStrings:Northstar");

// the host starts the container and fills the key for the application under test;
// the resource itself exposes the same started connection string
services.AddNorthstarDomain(options => options.UseNpgsql(database.ConnectionString));
```

`PostgresDatabase` is a run-scoped resource. `AddInfrastructure` is what starts it: the host starts the container once before the run, fills `ConnectionStrings:Northstar` with the started connection string, and releases the container when the host is disposed, after the run has stopped and the reports are written. `AddResource` alone only registers the resource for ownership and release; it starts nothing and fills no key. `TryStart` reports why a container could not start instead of throwing, so a machine without a container runtime can fall back to another database or skip the tests that need one.
