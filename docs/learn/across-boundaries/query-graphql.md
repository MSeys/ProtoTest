---
id: query-graphql
title: Write over REST, read over GraphQL
sidebar_position: 2
description: "Create data through REST and read it back through GraphQL in one test."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';

# Write over REST, read over GraphQL

<Lesson
  track="Across boundaries"
  step="Lesson 2 of 6"
  minutes={6}
  outcomes={[
    'Query a GraphQL endpoint from a test',
    'Describe the query and the expected answer with one object',
    'Check that a REST-created project appears through GraphQL',
  ]}
  needs={[
    <>The previous lesson, <a href="./call-an-api">Call an API and check its shape</a></>,
  ]}
/>

## The problem

Some applications offer the same data through more than one protocol. A bug can hide in the gap: the REST write succeeds, but GraphQL does not show the project. One test can catch that, because it can use both clients.

## Do it

### 1. Run the test alone

From the repository root, run the existing test in `samples/Northstar.ProtoTest/PlatformJourney.cs`:

```bash
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~RestWritesAreVisibleThroughGraphQL"
```

With the default sample settings, this test runs against the in-process application and should pass. You can also follow the excerpts with the saved trace in step 4.

The class selects `NorthstarTargets.Api` with `[Application]`. Its `[NorthstarMember(PlanIds.Growth)]` attribute provisions a tenant and installs the sample authenticator. The method's `[SignedInAs]` declares the test user.

### 2. Write through REST

```csharp
using var created = await Proto.Context.Rest()
    .Body(new CreateProjectRequest("atlas"))
    .PostAsync("/api/v1/projects");
created.Should.HaveHttpStatus(HttpStatusCode.Created);
```

This uses the same REST pattern as the previous lesson. The status check requires HTTP 201 before the test sends its GraphQL query.

### 3. Read through GraphQL

```csharp
using var projects = await Proto.Context.GraphQL()
    .Query("projects", new { first = 10 })
    .ExpectAsync(new
    {
        totalCount = 1,
        nodes = new[] { new { name = "atlas", status = ProjectStatuses.Active } }
    });

projects.Should.HaveNoErrors();
```

`Proto.Context.GraphQL()` creates a GraphQL request builder. `Query("projects", new { first = 10 })` selects the root field `projects` and supplies the `first` argument. ProtoTest derives the operation name `Projects` from that field name.

The object given to `ExpectAsync` does two jobs. Its structure supplies the selection set: the fields requested from the server. Here those fields are `totalCount` and `nodes` with `name` and `status`.

After sending the query, `ExpectAsync` matches `data.projects` against the expected object. It checks the count, name and active status shown above. This is a shape check of the selected data, not a comparison of the entire response envelope.

`ExpectAsync` does not reject GraphQL errors by itself. `HaveNoErrors()` checks that the response carries no GraphQL errors. A response can contain matching data alongside errors, even with HTTP 200.

### 4. Look at the trace

In the [viewer](https://trace.prototest.dev), select `RestWritesAreVisibleThroughGraphQL` and open Execution. Find the REST operation, then `GraphQL · query Projects`. The GraphQL checks are `Assert GraphQL data shape` and `Assert no GraphQL errors`.

Open the GraphQL request attachment to inspect `operationName: "Projects"` and the generated query. The response attachment contains `data.projects`, with `totalCount: 1` and one node named `atlas` with status `active`.

The saved trace is here: [l4-coverage.prototrace](pathname:///lessons/l4-coverage.prototrace). It records successful REST and GraphQL calls, each with `TestUserAuthenticator` and `NorthstarAuthenticator` operations. Fresh runs can have different ids and timings.

## What happened

Both clients use the same test context. In `Setup.cs`, REST and GraphQL belong to the `NorthstarTargets.Api` application, with GraphQL configured for `/graphql`. The test selects that application, and the sample authenticator supplies the same tenant member's bearer token to both requests.

That setup lets the test cross protocols without copying a token between clients. A shared context alone does not guarantee the same target or credentials. Client registration, application selection and authentication settings still determine those choices.

The tenant starts without projects, and this test creates one. The GraphQL resolver lists projects for the authenticated tenant, so projects in other tenants do not affect `totalCount = 1`. The test checks the project's name and status, but does not compare its REST and GraphQL ids.

## Check yourself

<Checkpoint question="A hand-written GraphQL test needs a query string and a separate assertion. Why does this test need only one object?">

ProtoTest derives the selection set from the expected object, then uses that object to check the selected data. This avoids repeating the field list in a separate query string. You still choose the root field, arguments and expected values, and check GraphQL errors separately.

</Checkpoint>

## Remember

- Configure each client for the intended application and authentication before comparing results across protocols.
- `ExpectAsync(shape)` derives selected fields and checks the returned data against the shape.
- Add `HaveNoErrors()`, because a GraphQL error can arrive with a 200 status.

Next: [Check what the application stored](./check-the-database.md).

## Go deeper

- [GraphQL integration](/docs/integrations/graphql): queries, mutations and subscriptions.
- [Queries and mutations](/docs/integrations/graphql/operations): variables and raw documents.
