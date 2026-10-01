---
id: the-trace-as-the-feedback-loop
title: Fix a failure with one change per run
sidebar_label: The trace as a feedback loop
sidebar_position: 3
description: "Use the trace to turn a failed run into one change, rerun, and compare the two traces."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';
import Link from '@docusaurus/Link';

# Fix a failure with one change per run

<Lesson
  track="Understand failures"
  step="Lesson 3 of 8"
  minutes={8}
  outcomes={[
    'Turn a failed check into one change to the test',
    'Rerun and confirm the change with the second trace',
    'Pick which of the four questions a failure left open',
  ]}
  needs={[
    <>The previous lesson, <Link to="/learn/understand-failures/read-a-failing-trace">read a failing trace</Link></>,
    'Nothing installed. The archives are on this site.',
  ]}
/>

## The problem

You can read a failing trace. Now use it to decide what to change. Changing several things at once makes the next result harder to explain.

The loop that works is short. Run the suite, open the trace, read the failing check, change one thing, run again and compare the two traces.

## Do it

### 1. Read the failing check

The visibility failure sent an empty project name and asserted the status:

```csharp
using var response = await Proto.Context.Rest()
    .Body(new CreateProjectRequest(""))
    .PostAsync("/api/v1/projects");

response.Should.HaveHttpStatus(HttpStatusCode.Created);
```

The check failed with a message starting `Expected HTTP status 201 (Created), but received 400 (BadRequest)`. That message also includes the problem body. Open [l0-visibility-drill.prototrace](pathname:///lessons/l0-visibility-drill.prototrace) in the [viewer](https://trace.prototest.dev) and look at the request entry. Its response attachment names `validation_failed` and the empty `name` parameter.

The test did not check the body, but the failed run already showed it. The application rejected an invalid request as expected.

### 2. Change one thing

Make this test check the rejection of an empty name. Expect HTTP 400 and check the problem code and message:

```csharp
response
    .Should.HaveHttpStatus(HttpStatusCode.BadRequest)
    .Should.MatchShape(new
    {
        code = ProblemCodes.ValidationFailed,
        message = JsonValue.StringContaining("name")
    });
```

The paired test sends the same empty-name request through the same composed client. Its expectation changes from successful creation to a specific validation failure.

Choose that expectation from the behavior you want to test. If a valid request should succeed, receiving HTTP 400 still needs investigation.

### 3. Run again and compare

Open [l0-visibility-fix.prototrace](pathname:///lessons/l0-visibility-fix.prototrace) beside the first trace. The request now has two passing checks: HTTP status and JSON shape. A later shape mismatch can identify which checked property differs.

### 4. When the trace is silent, the missing answer is the fix

The environment failure has no HTTP request operation in its trace. The execution entry ran about two seconds and failed with a connection error.

Check the source to explain the missing request. This test creates a raw `HttpClient` inside its body and calls a hardcoded address. That client bypasses ProtoTest's REST instrumentation. The paired test uses `Proto.Context.Rest()`, which takes its address from the composition and records the request.

## What happened

The visibility trace showed why the request failed. The environment trace showed the execution failure. The source explained why no request operation appeared.

Keep each change focused so you can compare its effect. IDs, timings and setup details can also differ between runs. Compare the relevant request and checks rather than expecting identical traces.

When a failure looks random, walk the four questions in order:

- Time: a real wait does not move the test clock. The fix advances it before reading the organization.
- State: the test guessed `prj_1`. The fix creates a project and checks its tenant's list.
- Environment: the address was hardcoded. The fix takes it from the composition.
- Visibility: the request had an empty name. The fix checks HTTP 400 and the validation problem body.

Use these questions to choose what to inspect next. A missing operation is a reason to check instrumentation and source, not a diagnosis by itself.

## Check yourself

<Checkpoint
  question="The environment trace has a connection error but no HTTP request operation. The visibility trace already shows the validation body. What should each test change?"
  verify={<>Compare <a href="pathname:///lessons/l0-environment-drill.prototrace">l0-environment-drill.prototrace</a> and <a href="pathname:///lessons/l0-visibility-drill.prototrace">l0-visibility-drill.prototrace</a> in the <a href="https://trace.prototest.dev">viewer</a>, then read the two fixes.</>}>

The environment test uses a raw client with a hardcoded address. Use the composed REST client to resolve the address and record the call.

The visibility test sent invalid input but expected successful creation. Check the intended rejection: HTTP 400, `validation_failed`, and a message mentioning `name`.

</Checkpoint>

## Remember

- Run, open the trace, read the failing check, change one thing, run again.
- A trace turns a failed run into facts you can act on without rerunning it.
- Use the four questions to choose what to inspect. Check source when the trace leaves something unexplained.

## Go deeper

- [The trace reference](/docs/observability/prototrace): every operation a trace records, and the viewer that draws it.
- [Findings](/learn/understand-failures/findings): the next lesson, for runs that fail while every test passes.
