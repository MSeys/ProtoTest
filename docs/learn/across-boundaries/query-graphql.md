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
    'Prove that two protocols see the same data',
  ]}
  needs={[
    <>The previous lesson, <a href="./call-an-api">Call an API and check its shape</a></>,
  ]}
/>

## The problem

Some applications offer the same data through more than one protocol. A bug can hide in the gap: the REST write succeeds, but GraphQL does not show the project. One test can catch that, because it can use both clients.

## Do it

### 1. Run the test alone

```bash
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~RestWritesAreVisibleThroughGraphQL"
```

### 2. Write through REST

```csharp
using var created = await Proto.Context.Rest()
    .Body(new CreateProjectRequest("atlas"))
    .PostAsync("/api/v1/projects");
created.Should.HaveHttpStatus(HttpStatusCode.Created);
```

This is the call from the previous lesson. The test checks the status so a failed write stops here, with a clear message, instead of showing up later as an empty query.

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

`Proto.Context.GraphQL()` is the GraphQL client. `Query("projects", new { first = 10 })` names the query field and passes its argument.

The object given to `ExpectAsync` does two jobs. ProtoTest builds the selection set from it, so the query asks for `totalCount` and `nodes` with `name` and `status`. Then it checks the answer against the same values.

`HaveNoErrors()` checks that the response carries no GraphQL errors. GraphQL can answer with HTTP 200 and still report errors, so this check matters.

### 4. Look at the trace

In the [viewer](https://trace.prototest.dev), the execution phase shows the REST operation, then `GraphQL · query Projects`, then the checks `Assert GraphQL data shape` and `Assert no GraphQL errors`. Both calls apply the same sign-in, so you see the authenticator entries twice.

The recorded run is here: [l4-coverage.prototrace](pathname:///lessons/l4-coverage.prototrace).

## What happened

Both clients came from the same test context, so they reached the same application as the same signed-in member. The test did not copy an id or a token from one client to the other.

`totalCount = 1` only holds because the test works on data of its own. Other tests run at the same time, and their projects must not appear in this list.

## Check yourself

<Checkpoint question="A hand-written GraphQL test needs a query string and a separate assertion. Why does this test need only one object?">

ProtoTest derives the selection set from the expected object. The fields you assert are the fields it asks for, so the query and the assertion cannot drift apart.

</Checkpoint>

## Remember

- One test can use several clients, and they share the test's identity.
- `ExpectAsync(shape)` is both the selection set and the assertion.
- Add `HaveNoErrors()`, because a GraphQL error can arrive with a 200 status.

Next: [Check what the application stored](./check-the-database.md).

## Go deeper

- [GraphQL integration](/docs/integrations/graphql): queries, mutations and subscriptions.
- [Queries and mutations](/docs/integrations/graphql/operations): variables and raw documents.
