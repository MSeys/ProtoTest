---
sidebar_position: 6
title: Written over REST, read over GraphQL
description: Create a project over REST and read it back through the GraphQL API of the same application, both clients on one in-process server.
---

import TraceExample from '@site/src/components/TraceExample';

# Written over REST, read over GraphQL

## The situation

Many applications write through REST and read through GraphQL. Testing each API alone misses the question that matters: does a write through one show up in the other?

Both clients can target the same application in one test. The GraphQL read checks the row the REST write created. The sample suite runs this journey in [PlatformJourney.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/Northstar.ProtoTest/PlatformJourney.cs).

## The code

### Compose

Both protocol clients register on the same application:

```csharp
// Setup.cs: one application, two protocols.
builder.AddApplication(NorthstarTargets.Api, app =>
{
    if (run.RunsLocalApplications)
    {
        app.AddAspNetCoreServer<NorthstarProgram>(configureWebHost: webHost =>
            ConfigureHostedApplication(webHost, run));
    }

    app.AddRest(rest => rest
            .CaptureAttachments()
            .AddClient(NorthstarTargets.Api))
        .AddGraphQL(graphQL => graphQL
            .CaptureAttachments()
            .AddClient("GraphQL", endpoint: "GraphQL"));
});
```

The GraphQL client uses the path in the application `Endpoints:GraphQL` key. With no path set it uses the transport root. Both clients reuse the in-process server's transport, so no socket is opened.

### The test

The test writes over REST and reads over GraphQL. It runs inside its own provisioned tenant, so the query's `totalCount = 1` is the test's own project:

```csharp
[Application(NorthstarTargets.Api)]
[NorthstarMember(PlanIds.Growth)]
public sealed class PlatformJourney
{
    [ProtoTest]
    [SignedInAs]
    public async Task RestWritesAreVisibleThroughGraphQL()
    {
        // Arrange: write through the public REST surface, so the read has something to prove.
        using var created = await Proto.Context.Rest()
            .Body(new CreateProjectRequest("atlas"))
            .PostAsync("/api/v1/projects");
        created.Should.HaveHttpStatus(HttpStatusCode.Created);

        // Act and assert: the selection is built from the shape, so the query asks for exactly the fields it checks.
        using var projects = await Proto.Context.GraphQL()
            .Query("projects", new { first = 10 })
            .ExpectAsync(new
            {
                totalCount = 1,
                nodes = new[] { new { name = "atlas", status = ProjectStatuses.Active } }
            });

        projects.Should.HaveNoErrors();
    }
}
```

`Query("projects", arguments).ExpectAsync(shape)` builds the selection from the shape and asserts against it in one step, so the query asks for exactly the fields the test checks. See [Shape-driven operations](../integrations/graphql/operations.md#shape-driven-operations).

The shape the test asserts, with what each field proves:

```json
{
  "totalCount": 1,          // only this test's project: the tenant holds nothing else
  "nodes": [
    {
      "name": "atlas",      // the REST write's name, read back over GraphQL
      "status": "active"    // the REST write's status, in GraphQL's terms
    }
  ]
}
```

## What the trace shows

Both calls are operations of the same test against the same server, in order:

- the REST write as `http.request` with `assert.http.status` and the `http.response` observation,
- the GraphQL read as `graphql.operation` with its selection, `assert.json.shape` and the `graphql.response` observation.

Order matters. A read failure points to the write before it. Both payloads stay in the same test trace.

The sample suite's own run:

<TraceExample
  demo="rest-graphql"
  title="REST write → GraphQL read"
  path="POST /api/v1/projects · query projects"
/>

## Variations

- **A published application.** Configure the application's `BaseUrl` and the same clients follow that address. The in-process server steps aside.
- **Read one value.** `ReadDataAs<T>("$.order.total")` reads a single JSON path when a full shape is more than the assertion needs. See [Responses](../integrations/graphql/responses.md).
- **Exact matching.** `MatchShape(shape, exact: true)` fails when the response carries a field the shape does not mention, so a forgotten field cannot slip past the assertion.

## What it does not prove

- **Filter to what the test created.** `totalCount = 1` only holds if the query is narrowed to this test's data. The sample suite gets that from its provisioned tenant, and a shared database needs its own filter.
- **The two APIs name things differently.** REST and GraphQL often disagree on casing and enum values (`active` and `ACTIVE`). Assert each in its own terms, because the shape matcher compares exactly.
- **Assume the read may lag the write until the test proves otherwise.** If the read side updates asynchronously, the first query can miss the write. Poll with a deadline instead of adding a delay, so the wait ends as soon as the write is visible. [`ProtoPolling.PollAsync`](../foundation/time.md#waiting-for-work-on-a-real-timer) is that loop.

  ```text
  BEFORE: await Task.Delay(2000); // slow, and still flaky when the write lags longer
  AFTER:  poll with a deadline until the row is visible // ends as soon as the write lands
  ```
- **Shape-driven queries ask for what the shape names.** A field the shape omits is not fetched, and a field the server does not have fails the query.
