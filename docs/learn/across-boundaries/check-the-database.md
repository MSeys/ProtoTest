---
id: check-the-database
title: Check what the application stored
sidebar_position: 3
description: "Create data through the API, then read the row from the database to prove it was committed."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';
import Link from '@docusaurus/Link';

# Check what the application stored

<Lesson
  track="Across boundaries"
  step="Lesson 3 of 6"
  minutes={7}
  outcomes={[
    'Read a row from the application database inside a test',
    'Skip a test with a reason when the suite cannot inspect the store',
    'Say why the sample keeps its writes instead of rolling them back',
  ]}
  needs={[
    <>The previous lesson, <Link to="/learn/across-boundaries/query-graphql">Write over REST, read over GraphQL</Link></>,
    'A sample checkout and the .NET SDK to run the command',
  ]}
/>

## The problem

An API can answer `201 Created` while its storage path is wrong. Reading the API again tests another application path. This lesson instead checks the row through a separate database connection.

## Do it

### 1. Run the test alone

```bash
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~AProjectCreatedThroughRestIsCommittedToTheDatabase"
```

From the repository root, use the sample's default settings, with no external application URL or database override. It hosts the application in-process and recreates its SQLite file before each run. No database server is needed.

The following excerpts come from `samples/Northstar.ProtoTest/DomainAccessJourney.cs`. Its class selects the Northstar API and provisions a tenant through `[NorthstarMember]`.

### 2. Create the project over REST

```csharp
const string projectName = "rest-to-store";

using var created = await Proto.Context.Rest()
    .Body(new CreateProjectRequest(projectName))
    .PostAsync("/api/v1/projects");
var project = created
    .Should.HaveHttpStatus(HttpStatusCode.Created)
    .ReadRequired<ProjectResponse>();
```

`ReadRequired<ProjectResponse>()` reads the JSON body into a typed object. The test needs `project.Id` for the next step.

### 3. Query the table

```csharp
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
```

`Proto.Context.SqlConnection()` returns the connection opened for this test during setup. ProtoTest registers its disposal with the test context. The database can still be shared with the application and other tests.

The connection is an ordinary ADO.NET `DbConnection`. This command reads only the project id returned by the API, using a parameter rather than inserting it into SQL text.

### 4. Assert the row

```csharp
await using var stored = await command.ExecuteReaderAsync();
Assert.That(await stored.ReadAsync(), Is.True, "The REST write did not create a project row.");
using (Assert.EnterMultipleScope())
{
    Assert.That(stored.GetString(0), Is.EqualTo(project.Id));
    Assert.That(stored.GetString(1), Is.EqualTo(projectName));
    Assert.That(stored.GetString(2), Is.EqualTo(ProjectStatuses.Active));
}
```

The first NUnit assertion checks that a row exists. The multiple scope then checks its id, name and status together.
The result shows that this API write is visible through the test's separate connection. It does not establish the database's behavior after a crash.

### 5. See the skip rule

The test carries this attribute:

```csharp
[RequiresCapability(ProtoCapabilityKinds.Store, Reason = "The suite does not own the store, so it cannot inspect it.")]
```

The `Store` capability describes registered database support. The sample registers SQL only when it hosts the application, has a configured store connection, or selects PostgreSQL.
Without that registration, the capability check skips this test before setup and prints the reason above.

The reason's ownership wording is specific to the sample. A suite can inspect an externally owned database when it has a connection and permission. Registered support also does not prove that the connection will open successfully.

## What happened

The test writes through the API and reads through a separate connection to the same store. With default settings, the trace records `SQL · open SqliteConnection` during setup and the REST call during execution.

The raw ADO.NET command does not produce a separate query operation here. Plain NUnit checks also do not each become ProtoTest check operations. A failure still appears in the test's outcome and error.

ProtoTest can keep each test's database writes apart in two ways. With `Transaction`, the SQL integration's default, it wraps the test's work on the test's connection in a transaction and rolls it back afterwards. With `None`, it does neither, and the test cleans up itself.

This sample chooses `None`, because the application writes through its own connection. A rollback on the test's connection could not undo those writes. Instead, the tenant provisioner deletes the tenant and all its data after the test.

ProtoTest also refuses `Transaction` at startup when an application is registered, unless `ShareConnectionWith` declares that the application really uses the test's connection.

## Check yourself

<Checkpoint question="This test only reads. Why does the sample's setup still choose SqlIsolation.None?">

The application uses a separate connection, so the test's transaction would not roll back its writes. Transaction isolation rejects registered applications that are not declared as sharing the connection.

Local provisioners write through the application's store. Domain composition against a configured external application uses the test's connection, whose writes must be visible to the application.
The sample therefore uses `None` with explicit tenant cleanup.

</Checkpoint>

## Remember

- Check the returned project id through a separate connection when testing the storage path.
- The test owns its connection, while the database can be shared or externally owned.
- Declare the `Store` requirement and register explicit cleanup when using `SqlIsolation.None`.

Next: [Drive the browser](./drive-the-browser.md).

## Go deeper

- [SQL integration](/docs/integrations/sql): isolation, Entity Framework Core and a database the run owns.
- [Skip conditions](/docs/foundation/skip-conditions): how a missing capability skips a test.
