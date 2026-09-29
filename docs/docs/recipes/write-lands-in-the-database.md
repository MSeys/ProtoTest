---
sidebar_position: 3
title: A write lands in the database
description: Create a project over REST, then read the committed row through the suite's own connection to the store the application writes to.
---

# A write lands in the database

## The situation

The API answers `201 Created`, but did the project reach the store in the state the response promised? The response body is not the row; it is the API's rendering of it.

The test creates the project over REST and then reads the committed row itself, through a connection the suite owns to the same database the application writes to. The demo runs this journey in [DomainAccessJourney.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/Northstar.ProtoTest/DomainAccessJourney.cs).

## The code

### Compose

The run owns the store. The demo uses a SQLite file that every run recreates, and switches to a PostgreSQL container when the environment asks for it:

```csharp
// Setup.cs: PostgreSQL when the run asks for it; a configured key skips the container.
if (run.OwnsPostgres)
{
    builder.AddInfrastructure(
        "NorthstarDatabase",
        chain => chain
            .UseConfigured()
            .UseContainer(PostgresDatabase.Container()),
        "ConnectionStrings:Northstar");
}
```

The test side is an ordinary SQL connection over the same address. The application commits on its own connection, so the test connection must not wrap its reads in a transaction:

```csharp
// Setup.cs: the suite's connection to the store the application also uses.
builder
    .AddSql(
        provider => CreateDatabaseConnection(
            ResolveDatabase(provider, run.DatabaseConnection ?? string.Empty),
            run.UsesPostgres),
        sql => sql.Isolation = SqlIsolation.None)
    .ConfigureServices(services =>
        services.AddNorthstarDomain(
            (provider, options) =>
            {
                var connection = provider.GetRequiredService<DbConnection>();
                if (run.UsesPostgres)
                {
                    options.UseNpgsql(connection);
                }
                else
                {
                    options.UseSqlite(connection);
                }
            },
            ServiceLifetime.Scoped));
```

### The test

The test writes over REST and reads the row with the demo's own SQL:

```csharp
[Application(NorthstarTargets.Api)]
[NorthstarMember(PlanIds.Growth)]
public sealed class DomainAccessJourney
{
    [ProtoTest]
    [SignedInAs]
    [RequiresCapability(ProtoCapabilityKinds.Store, Reason = "The suite does not own the store, so it cannot inspect it.")]
    public async Task AProjectCreatedThroughRestIsCommittedToTheDatabase()
    {
        const string projectName = "rest-to-store";

        using var created = await Proto.Context.Rest()
            .Body(new CreateProjectRequest(projectName))
            .PostAsync("/api/v1/projects");
        var project = created
            .Should.HaveHttpStatus(HttpStatusCode.Created)
            .ReadRequired<ProjectResponse>();

        await using var command = Proto.Context.SqlConnection().CreateCommand();
        command.CommandText = """
            SELECT "Id", "Name", "Status"
            FROM "Projects"
            WHERE "Id" = @id
            """;
        var id = command.CreateParameter();
        id.ParameterName = "@id";
        id.Value = project.Id;
        command.Parameters.Add(id);

        await using var stored = await command.ExecuteReaderAsync();
        Assert.That(await stored.ReadAsync(), Is.True, "The REST write did not create a project row.");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored.GetString(0), Is.EqualTo(project.Id));
            Assert.That(stored.GetString(1), Is.EqualTo(projectName));
            Assert.That(stored.GetString(2), Is.EqualTo(ProjectStatuses.Active));
        }
    }
}
```

`Proto.Context.SqlConnection()` resolves the connection the test owns over the store the application writes to, so both sides meet on one address. See [SQL](../integrations/sql/index.md) for the accessors and the isolation rules.

## What the trace shows

- The REST `http.request` for the write, with `assert.http.status` and the response as an `http.response` observation.
- The `sql.connection.open` operation in setup, with the connection type, and the connection's release at teardown.

The trace does not show the `SELECT`. Individual commands are not traced, so the trace proves the connection's lifecycle and the order of the calls, while the assertion proves the row. When the row is missing, the assertion fails with the message the test wrote. The long-form reading is on [ProtoTrace](../observability/prototrace.md).

## Variations

- **Entity Framework Core.** `AddEntityFrameworkCore<TContext>` reads the declared SQL keys, so the context joins the test's connection. With the default `SqlIsolation.Transaction` the read joins the test's transaction and records `sql.enlist`; with `None` there is no transaction and no enlistment. See [Entity Framework Core](../integrations/sql/index.md#entity-framework-core).
- **A real PostgreSQL.** Set the demo's environment switch (`ProtoTest__Database=postgres`) and the run starts the container; a configured connection string skips it.
- **The application shares the connection.** When the application is declared with `ShareConnectionWith(...)`, the test's transaction can cover both sides, and a rollback at teardown undoes the application's write too. See [Isolation](../integrations/sql/index.md#isolation).
- **Read through the application's context.** With `AddEntityFrameworkCore<NorthstarDbContext>`, the test reads with the same mapping the application wrote with, instead of hand-written SQL.

## What it does not prove

- **Why `SqlIsolation.None` here.** The in-process application opens its own connection and commits; a rollback on the test's connection would not undo that write. With the default `Transaction` isolation the run-start guard throws while an application registered through `AddApplication` is not declared with `ShareConnectionWith(...)`, and a declaration is a statement, not enforcement.
- **Unique values, not cleanup.** A container lives for one run and the SQLite file is recreated, so nothing survives to the next run. Within a run, a reference built from `TestId` keeps parallel tests out of each other's rows.
- **No SQL tracing.** The connection and transaction lifecycle is traced; individual commands are not.
- **Against a deployed environment the suite usually cannot reach the database.** Compose `AddSql` only where it can, and gate the test with `[RequiresCapability(ProtoCapabilityKinds.Store)]`: where no store is composed, it skips instead of failing.
