---
sidebar_position: 3
title: A write lands in the database
description: Create a project over REST, then read the committed row through the suite's own connection to the store the application writes to.
---

import TraceExample from '@site/src/components/TraceExample';
import AnnotatedCode from '@site/src/components/AnnotatedCode';

# A write lands in the database

`201 Created` is the response rendering. `SELECT` the row is the committed truth.

## The situation

The API answers `201 Created`, but did the project reach the store in the state the response promised? The response body is not the row. It is how the API describes the row.

The test creates the project over REST and then reads the committed row itself, through a connection the suite owns to the same database the application writes to. The demo runs this journey in [DomainAccessJourney.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/Northstar.ProtoTest/DomainAccessJourney.cs).

## The test

The test writes over REST and reads the row with the demo's own SQL:

<AnnotatedCode
  filename="DomainAccessJourney.cs"
  code={`[Application(NorthstarTargets.Api)]
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
}`}
  callouts={[
    {
      line: 16,
      title: 'The write landed',
      note: 'HaveHttpStatus checks the response. ReadRequired reads the created project for its id.',
    },
    {
      line: 31,
      title: 'The row exists',
      note: 'ReadAsync fails with the message the test wrote when the REST write created no row.',
    },
    {
      line: 34,
      title: 'The row matches',
      note: 'One scope checks id, name and status together, so a mismatch lists every column that differs.',
    },
  ]}
/>

`Proto.Context.SqlConnection()` returns the test connection to the same store the application writes to. See [SQL](../integrations/sql/index.md) for the accessors and the isolation rules.

| What the test proves | Where it lands |
| --- | --- |
| `POST /api/v1/projects` returns `201 Created` | The response body names `rest-to-store` |
| `SELECT` finds the row | `Id`, `Name` = `rest-to-store`, `Status` = `active` |
| The row is committed, not staged | The read runs outside any transaction |

## Compose

The run owns the store. The demo uses a SQLite file that every run recreates, and switches to a PostgreSQL container when the environment asks for it:

```text
[default] SQLite file, recreated per run | [ProtoTest__Database=postgres] container, skipped if configured
```

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

## What the trace shows

- The REST `http.request` for the write, with `assert.http.status` and the response as an `http.response` observation.
- The `sql.connection.open` operation in setup, with the connection type, and the connection's release at teardown.

The trace does not show the `SELECT`. Individual commands are not traced. The trace shows the connection lifecycle and call order. The assertion checks the row. When the row is missing, the assertion fails with the message the test wrote. The long-form reading is on [ProtoTrace](../observability/prototrace.md).

In short, the trace reads in test order:

```text
01 http.request POST /api/v1/projects -> assert.http.status
02 sql.connection.open in setup, released at teardown
```

The `SELECT` itself is not traced. The connection at `02` proves the lifecycle. The assertion proves the row.

The demo's own run:

<TraceExample
  demo="rest-database"
  title="REST write → committed row"
  path="POST /api/v1/projects · SELECT the row"
/>

## Variations

| When | Option | What changes |
| --- | --- | --- |
| The test reads through Entity Framework Core | `AddEntityFrameworkCore<TContext>` | The context joins the test connection. The default `Transaction` isolation enlists the read and records `sql.enlist`. `None` skips the transaction. See [Entity Framework Core](../integrations/sql/index.md#entity-framework-core). |
| The run should use a real PostgreSQL | Set `ProtoTest__Database=postgres` | The run starts the container. A configured connection string skips it. |
| The application should share the test transaction | Declare `ShareConnectionWith(...)` | The test's transaction can cover both sides, and a rollback at teardown undoes the application's write too. See [Isolation](../integrations/sql/index.md#isolation). |
| The test should read with the application's mapping | `AddEntityFrameworkCore<NorthstarDbContext>` | The test reads with the same mapping the application wrote with, instead of hand-written SQL. |

## What it does not prove

- **The test reads outside any transaction.** The in-process application opens its own connection and commits; a rollback on the test's connection would not undo that write. With the default `Transaction` isolation the run fails at startup unless the application declares `ShareConnectionWith(...)`. That declaration is a statement, not a check.
- **Unique values, not cleanup.** A container lives for one run and the SQLite file is recreated, so nothing survives to the next run. Within a run, a reference built from `TestId` keeps parallel tests out of each other's rows.
- **No SQL tracing.** The connection and transaction lifecycle is traced; individual commands are not.
- **In a deployed environment the suite often cannot reach the database.** Compose `AddSql` only where it can, and gate the test with `[RequiresCapability(ProtoCapabilityKinds.Store)]`: where no store is composed, it skips instead of failing.
