---
id: call-an-api
title: Call an API and check its shape
sidebar_position: 1
description: "Send one REST request from a test and check the status and the shape of the response."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';

# Call an API and check its shape

<Lesson
  track="Across boundaries"
  step="Lesson 1 of 6"
  minutes={7}
  outcomes={[
    'Send a request to a running application from a test',
    'Check the status and the JSON shape in one chain',
    'Test a refusal the same way as a success',
  ]}
  needs={['The Start track, or a clone of the repository with the sample building']}
/>

## The problem

Most applications you test have an HTTP API. A good test calls it the way a client does and checks what comes back. Checking only the status code misses a wrong body. Comparing the whole body as text breaks when an id or a timestamp changes.

ProtoTest checks the shape: the fields you care about, with the exact values you know and a placeholder for the ones you do not.

## Do it

### 1. Run the test alone

```bash
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~CreatingAProjectReturnsIt"
```

It should report one passed test.

### 2. Read the call

The test creates a project in the Northstar sample application:

```csharp
var name = $"atlas-{Proto.Context.TestId}";
using var created = await Proto.Context.Rest()
    .Body(new CreateProjectRequest(name))
    .PostAsync("/api/v1/projects");
```

`Proto.Context` is the test context: the object that holds everything this one test uses. `Rest()` returns the REST client for the application the class selected with `[Application(NorthstarTargets.Api)]`. The client already knows the address, so the test passes only a path.

The name includes `TestId`, so two runs of the test never create the same project.

### 3. Check the response

```csharp
created
    .Should.HaveHttpStatus(HttpStatusCode.Created)
    .Should.MatchShape(new
    {
        id = JsonValue.NotNull(),
        name,
        status = ProjectStatuses.Active,
        environmentCount = 0
    });
```

Read the shape as a description of the JSON. `name`, `status` and `environmentCount` must equal the values given. `id` only has to exist, because the application chooses it.

### 4. See the refusals

The same file tests the failures. `AnEmptyProjectNameIsRefused` posts an empty name and expects `400 Bad Request` with `code = ProblemCodes.ValidationFailed` in the body. `AViewerCannotCreateProjects` signs in as a viewer and expects `403 Forbidden`. A refusal is an ordinary response, so you check it with the same two calls.

### 5. Look at the trace

Open the `.prototrace` file the run wrote under `TestResults/` in the [viewer](https://trace.prototest.dev). The execution phase of this test holds an operation `REST · POST /api/v1/projects`, then the checks `Assert status · 201 Created` and `Assert response shape`. An operation is one recorded step of a test, and a check is an assertion as the trace records it.

The recorded run is also here: [l1-first-journey.prototrace](pathname:///lessons/l1-first-journey.prototrace).

## What happened

The host built the REST client once, from the composition in the sample's setup class. Each test got its own client through its context. The request and the response were recorded as operations, so a failure shows the exact request that was sent.

A shape mismatch lists every wrong field with its JSON path at once, so you can fix all of them in one pass.

## Check yourself

<Checkpoint question="The application adds a field createdAt to the response. Does this test fail?">

No. The reference page describes `MatchShape` as comparing the fields you name. It has an `exact` overload for the stricter check, which also rejects fields you did not name.

</Checkpoint>

## Remember

- `Proto.Context.Rest()` gives a client that already knows the address.
- `Should.HaveHttpStatus(...)` and `Should.MatchShape(...)` chain on the response.
- Use `JsonValue.NotNull()` for values the application chooses.

Next: [Write over REST, read over GraphQL](./query-graphql.md).

## Go deeper

- [REST integration](/docs/integrations/rest): requests, authentication and attachments.
- [Responses and assertions](/docs/integrations/rest/responses): every assertion and the exact mode.
