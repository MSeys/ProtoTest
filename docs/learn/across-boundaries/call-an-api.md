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

In this lesson, you run a REST test and check its response shape. The shape names fields, expected values and constraints for values the application chooses.

The test is the one you wrote in [Write your first test](/learn/start/write-your-first-test). There it showed how a test runs. Here it shows how to check a response shape and a refusal.

## Do it

### 1. Run the test alone

From the repository root:

```bash
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName=Northstar.ProtoTest.ProjectsJourney.CreatingAProjectReturnsIt"
```

It should report one passed test. The full name selects the sample test even if you kept `MyFirstJourney.cs` from the Start track.

### 2. Read the call

The test in `samples/Northstar.ProtoTest/ProjectsJourney.cs` creates a project in the Northstar sample application:

```csharp
var name = $"atlas-{Proto.Context.TestId}";
using var created = await Proto.Context.Rest()
    .Body(new CreateProjectRequest(name))
    .PostAsync("/api/v1/projects");
```

`Proto.Context` is the test context, which holds this test's clients and state. `Rest()` starts a request using the REST client for the application selected by `[Application(NorthstarTargets.Api)]`.
The client already has a base address, so the test passes a path.

The name includes `TestId`, which distinguishes tests within the run. It does not guarantee uniqueness across runs.
Northstar also provisions a tenant for each test and registers its cleanup.

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

Read the shape as a description of the JSON. `name`, `status` and `environmentCount` must equal the values given.
`id` must exist and have a non-null value. `JsonValue.NotNull()` does not check its type, format or whether a string is empty.

By default, the shape allows additional object fields. It checks the named fields rather than comparing the response as raw text.

### 4. See the refusals

The same file tests refusals. `AnEmptyProjectNameIsRefused` posts an empty name and expects `400 Bad Request` with `code = ProblemCodes.ValidationFailed` in the body.
`AViewerCannotCreateProjects` signs in as a viewer and expects `403 Forbidden` with `code = ProblemCodes.Forbidden`.
These refusals return HTTP responses, so the tests check both status and body. A connection failure instead throws before a response is available.

### 5. Look at the trace

Find the newest `.prototrace` file under `samples/Northstar.ProtoTest/bin/Debug/net8.0/TestResults/` and open it in the [viewer](https://trace.prototest.dev).
Select `ProjectsJourney.CreatingAProjectReturnsIt`. Its execution phase contains `REST · POST /api/v1/projects`, followed by `Assert status · 201 Created` and `Assert response shape`.
An operation is one recorded step. A check is an assertion recorded in the trace.

The recorded run is also here: [l1-first-journey.prototrace](pathname:///lessons/l1-first-journey.prototrace).

## What happened

The setup class registered the clients. The framework initializes a client for each test context, which owns its cleanup.
In the default local sample, the HTTP client sends requests through an in-process test server's handler. Its base address does not require a network listener.

The trace records the HTTP exchange as one operation and the assertions as separate checks. The sample enables `CaptureAttachments()`, whose defaults capture request bodies, response bodies and expected shapes.
Text diagnostics apply the configured redaction rules. The trace is diagnostic evidence, not an exact copy of everything sent over the transport.

The shape matcher collects mismatches and identifies their JSON paths. These are differences against the expected shape, not every possible defect in the response.

## Check yourself

<Checkpoint question="The application adds a field createdAt to the response. Does this test fail?">

No. `MatchShape` allows additional object fields by default. Passing `exact: true` also rejects fields that the shape leaves unmentioned.
Value constraints still define what they check. For example, `JsonValue.NotNull()` accepts an entire non-null object without checking its children, even in exact mode.

</Checkpoint>

## Remember

- `Proto.Context.Rest()` gives a client that already knows the address.
- `Should.HaveHttpStatus(...)` and `Should.MatchShape(...)` chain on the response.
- Use `JsonValue.NotNull()` when presence and a non-null value are enough. Use a stronger constraint when type or format matters.

Next: [Write over REST, read over GraphQL](./query-graphql.md).

## Go deeper

- [REST integration](/docs/integrations/rest): requests, authentication and attachments.
- [Responses and assertions](/docs/integrations/rest/responses): every assertion and the exact mode.
