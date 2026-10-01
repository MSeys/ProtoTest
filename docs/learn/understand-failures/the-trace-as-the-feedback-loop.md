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

You can read a failing trace. Now you have to fix the test, and a change that touches three things at once hides the next failure.

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

The check failed with `Expected HTTP status 201 (Created), but received 400 (BadRequest)`. Open [l0-visibility-drill.prototrace](pathname:///lessons/l0-visibility-drill.prototrace) in the [viewer](https://trace.prototest.dev) and look at the request entry. The response body is attached, and it names `validation_failed` and the empty parameter.

The test never looked at that body. The answer was in the failed run all along.

### 2. Change one thing

Assert what the application said, not only the status:

```csharp
response
    .Should.HaveHttpStatus(HttpStatusCode.BadRequest)
    .Should.MatchShape(new
    {
        code = ProblemCodes.ValidationFailed,
        message = JsonValue.StringContaining("name")
    });
```

Nothing else changed. The request is the same and the address is the same.

### 3. Run again and compare

Open [l0-visibility-fix.prototrace](pathname:///lessons/l0-visibility-fix.prototrace) beside the first trace. Look for two checks, both recorded, where there was one. If the check fails again, its message now names the code and the parameter.

### 4. When the trace is silent, the missing answer is the fix

The environment failure has no request in its trace. The execution entry ran about two seconds and failed with a connection error. The call happened outside the run, so nothing wrapped it. The change is to call through `Proto.Context.Rest()` like every other journey.

## What happened

You used the trace in two ways. In the visibility case it held the evidence the test ignored. In the environment case its silence was the evidence.

The change was small on purpose. One change per run means the new trace differs from the old one in one place, so a pass or a new failure points at that change.

When a failure looks random, walk the four questions in order:

- Time: a real wait does not move the test clock. The fix advances the clock.
- State: the id belonged to no test in the run. The fix creates and reads its own data.
- Environment: the address was hardcoded. The fix takes it from the composition.
- Visibility: the test read the status and ignored the body. The fix asserts the body.

A trace usually answers three of them. The one it stays silent on is the one to fix.

## Check yourself

<Checkpoint
  question="The environment failure ended in a connection error and its trace holds no request. The visibility failure ended on the status alone, and its trace holds the response body. Both fixes call the composed client. What did each failure leave unanswered?"
  verify={<>Compare <a href="pathname:///lessons/l0-environment-drill.prototrace">l0-environment-drill.prototrace</a> and <a href="pathname:///lessons/l0-visibility-drill.prototrace">l0-visibility-drill.prototrace</a> in the <a href="https://trace.prototest.dev">viewer</a>, then read the two fixes.</>}>

The environment test ran outside the composition, so the failure was recorded and the call was not. Its answer is to take the address from the composition.

The visibility test ran inside it, so the request and the response were recorded. It asserted the status alone and never read the body. Its answer is to read what the application said.

</Checkpoint>

## Remember

- Run, open the trace, read the failing check, change one thing, run again.
- A trace turns a failed run into facts you can act on without rerunning it.
- The four questions say where to look. The trace stays silent on the answer that is missing.

## Go deeper

- [The trace reference](/docs/observability/prototrace): every operation a trace records, and the viewer that draws it.
- [Findings](/learn/understand-failures/findings): the next lesson, for runs that fail while every test passes.
