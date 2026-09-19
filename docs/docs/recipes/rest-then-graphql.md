---
sidebar_position: 6
title: Written over REST, read over GraphQL
description: Create a project over REST and read it back through the GraphQL API of the same application, both clients on one in-process server.
---

# Written over REST, read over GraphQL

Many applications write through REST and read through GraphQL. Testing each API alone misses the question that matters: does a write through one show up in the other? Both clients can target the same application in one test.

The same journey runs in the demo — [PlatformJourney.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/PlatformJourney.cs); its host composes both clients in [Setup.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/Setup.cs). The full API surface is in [GraphQL](../integrations/graphql/index.md).

## Compose

```csharp
protected override void Configure(IProtoHostBuilder builder) =>
    builder.AddApplication("Api", app => app
        .AddAspNetCoreServer<Program>()
        .AddRest(rest => rest.AddClient("Api"))
        .AddGraphQL(graphQL => graphQL.AddClient("GraphQL")));
```

Both clients reuse the in-process server's transport. The GraphQL client is rooted at the path configured under `ProtoTest:Applications:Api:Endpoints:GraphQL` — the demo sets it to `/graphql`; with no path configured, the transport root is used.

## The test

```csharp
[Application("Api")]
public sealed class ProjectReadModelTests
{
    [ProtoTest]
    public async Task A_project_created_over_REST_is_queryable_over_GraphQL()
    {
        var name = $"atlas-{Proto.Context.TestId}";
        using var created = await Proto.Context.Rest()
            .Body(new { name })
            .PostAsync("/api/projects");
        created.Should.HaveHttpStatus(HttpStatusCode.Created);

        using var projects = await Proto.Context.GraphQL()
            .Query("projects", new { filter = new { name } })
            .ExpectAsync(new
            {
                totalCount = 1,
                nodes = new[] { new { name, status = "ACTIVE" } }
            });

        projects.ShouldHaveNoErrors();
    }
}
```

`Query("projects", arguments).ExpectAsync(shape)` builds the selection from the shape and asserts against it in one step, so the query asks for exactly the fields the test checks. See [Shape-driven operations](../integrations/graphql/operations.md#shape-driven-operations).

## What it proves

The REST write and the GraphQL read are two operations of the same test against the same server, in order — so the test proves the read side sees the write, not just that each API works in isolation. When the shape does not match, the failure shows expected against actual for every property.

## Limits

- **Filter to what the test created.** `totalCount = 1` only holds if the query is narrowed to this test's project; parallel tests create projects too.
- **The two APIs name things differently.** REST and GraphQL often disagree on casing and enum values (`active` and `ACTIVE`). Assert each in its own terms; the shape matcher compares exactly.
- **Read-your-writes is an assumption until proven.** If the read side is updated asynchronously, the first query can miss the write. Assert on that explicitly — poll with a deadline — rather than adding a delay.
- **Shape-driven queries ask for what the shape names.** A field the shape omits is not fetched, and a field the server does not have fails the query.
