---
sidebar_position: 6
title: Written over REST, read over GraphQL
description: Create a project over REST and read it back through the GraphQL API of the same application, both clients on one in-process server.
---

import TraceExample from '@site/src/components/TraceExample';

# Written over REST, read over GraphQL

## The situation

Many applications write through REST and read through GraphQL. Testing each API alone misses the question that matters: does a write through one show up in the other?

Both clients can target the same application in one test, so the read side is proven against the write this test just made. The demo runs this journey in [PlatformJourney.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/Northstar.ProtoTest/PlatformJourney.cs).

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

The GraphQL client is rooted at the path configured under the application's `Endpoints:GraphQL` key (`ProtoTest:Applications:Northstar:Endpoints:GraphQL` in the demo); with no path configured the transport root is used. Both clients reuse the in-process server's transport, so no socket is opened.

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
        // Arrange: write through the public REST surface.
        using var created = await Proto.Context.Rest()
            .Body(new CreateProjectRequest("atlas"))
            .PostAsync("/api/v1/projects");
        created.Should.HaveHttpStatus(HttpStatusCode.Created);

        // Act
        using var projects = await Proto.Context.GraphQL()
            .Query("projects", new { first = 10 })
            .ExpectAsync(new
            {
                totalCount = 1,
                nodes = new[] { new { name = "atlas", status = ProjectStatuses.Active } }
            });

        // Assert
        projects.Should.HaveNoErrors();
    }
}
```

`Query("projects", arguments).ExpectAsync(shape)` builds the selection from the shape and asserts against it in one step, so the query asks for exactly the fields the test checks. See [Shape-driven operations](../integrations/graphql/operations.md#shape-driven-operations).

## What the trace shows

Both calls are operations of the same test against the same server, in order:

- the REST write as `http.request` with `assert.http.status` and the `http.response` observation,
- the GraphQL read as `graphql.operation` with its selection, `assert.json.shape` and the `graphql.response` observation.

The order is the proof: a failure in the read points at the write that should have caused it, and both payloads sit in the same test's trace.

The demo's own run:

<TraceExample
  demo="rest-graphql"
  title="REST write → GraphQL read"
  path="POST /api/v1/projects · query projects"
/>

## Variations

- **A published application.** Configure the application's `BaseUrl` and the same clients follow that address; the in-process server steps aside.
- **Read one value.** `ReadDataAs<T>("$.order.total")` reads a single JSON path when a full shape is more than the assertion needs. See [Responses](../integrations/graphql/responses.md).
- **Exact matching.** `MatchShape(shape, exact: true)` fails when the response carries a field the shape does not mention, so a forgotten field cannot slip past the assertion.
- **A read-your-writes loop.** Where the read side updates asynchronously, poll with a deadline instead of adding a delay, so the wait ends as soon as the write is visible.

## What it does not prove

- **Filter to what the test created.** `totalCount = 1` only holds if the query is narrowed to this test's data. The demo gets that from its provisioned tenant; a shared database needs its own filter.
- **The two APIs name things differently.** REST and GraphQL often disagree on casing and enum values (`active` and `ACTIVE`). Assert each in its own terms; the shape matcher compares exactly.
- **Read-your-writes is an assumption until proven.** If the read side is updated asynchronously, the first query can miss the write. Assert on that explicitly instead of adding a delay.
- **Shape-driven queries ask for what the shape names.** A field the shape omits is not fetched, and a field the server does not have fails the query.
