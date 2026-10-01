---
id: check-the-database
title: Check what the application stored
sidebar_position: 3
description: "Create data through the API, then read the row from the database to prove it was committed."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';

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
    <>The previous lesson, <a href="./query-graphql">Write over REST, read over GraphQL</a></>,
  ]}
/>

## The problem

An API can answer `201 Created` and still not have stored the row. A test that reads the API again trusts the same code that made the claim. To be sure, the test asks the database itself.

## Do it

### 1. Run the test alone

```bash
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~AProjectCreatedThroughRestIsCommittedToTheDatabase"
```

By default the sample hosts the application in the test process and uses a SQLite file that each run creates fresh. No database server is needed.

### 2. Create the project over REST

```csharp
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

`Proto.Context.SqlConnection()` returns a database connection that belongs to this test. It is an ordinary ADO.NET `DbConnection`, so you write an ordinary command with a parameter.

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

These are plain NUnit assertions. The first one carries a message that says what a failure means. The multiple scope reports every wrong column together.

### 5. See the skip rule

The test carries this attribute:

```csharp
[RequiresCapability(ProtoCapabilityKinds.Store, Reason = "The suite does not own the store, so it cannot inspect it.")]
```

A capability is what an integration lets a test do once it is added to the host. In the sample, the setup class adds the SQL connection only when `run.CanComposeDomain` is true, which is the case when the suite hosts the application itself or has a store address. Without it, this test skips and names the reason instead of failing.

## What happened

The test went through two doors into the same system, the API and the database. The trace records `SQL · open SqliteConnection` in the setup phase and the REST call in the execution phase.

The sample registers the connection with `SqlIsolation.None`. A comment in the setup class gives the reason: the application has its own connection, so a test transaction would hide fixture writes. The SQL reference adds that with the default `Transaction` isolation, writes through the owned connection roll back at teardown, while writes through the application's connection commit.

## Check yourself

<Checkpoint question="This test only reads. Why does the sample's setup still choose SqlIsolation.None?">

The same connection is used to create fixtures through the domain. The application writes on its own connection, and a transaction on the test's connection would hide those fixture writes from it. The setup class says so in a comment.

</Checkpoint>

## Remember

- Ask the database when the claim is "it was stored".
- `Proto.Context.SqlConnection()` is an ordinary `DbConnection` owned by the test.
- A test that needs the store declares the `Store` capability and skips with a reason where there is none.

Next: [Drive the browser](./drive-the-browser.md).

## Go deeper

- [SQL integration](/docs/integrations/sql): isolation, Entity Framework Core and a database the run owns.
- [Skip conditions](/docs/foundation/skip-conditions): how a missing capability skips a test.
