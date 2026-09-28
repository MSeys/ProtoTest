---
id: the-trace-as-the-feedback-loop
title: The trace as a feedback loop
sidebar_label: The trace as a feedback loop
sidebar_position: 4
description: "Use the trace to read a failed run, narrow the fix to one answer, and rerun with evidence."
---

import LearnShell from '@site/src/components/LearnShell';
import Link from '@docusaurus/Link';

# The trace as a feedback loop

A failed run is only useful if you can read it. The loop that makes an integration suite trustworthy is short: run it, open the trace, read the check that failed, change one thing, run again.

<LearnShell
  level="Level 0, lesson 4"
  minutes="About 8 minutes"
  outcome={[
    'Read a failure from its trace without rerunning it.',
    'Name which of the four questions the failure left open.',
    'Describe the loop: run, read, change one thing, run again.',
  ]}
  before={[
    <>What a test leaves behind (<Link to="/learn/why-integration-tests-get-hard/what-a-test-leaves-behind">lesson 3</Link>).</>,
    'Nothing installed. The archives are on this site.',
  ]}
  situation={
    <>
      <p>A test failed in CI and the log shows one line of output. You cannot attach a debugger to the runner, and rerunning the job tells you the same thing. The trace is what is left of the run: what it wrapped, what it recorded, and what the check read.</p>
      <p>The four drills in this level end in exactly that kind of check. Reading one is the skill this level has been building towards.</p>
    </>
  }
  checkpoint={{
    question:
      'The environment drill failed with a connection error and its trace holds no request. The visibility drill failed on the status alone, and its trace holds the response body as an attachment. Both fixes call the composed client. What did each drill fail to answer?',
    verify: (
      <>
        Compare <a href="pathname:///lessons/l0-environment-drill.prototrace">l0-environment-drill.prototrace</a> and{' '}
        <a href="pathname:///lessons/l0-visibility-drill.prototrace">l0-visibility-drill.prototrace</a> in the{' '}
        <a href="https://trace.prototest.dev">viewer</a>, then read the two fixes.
      </>
    ),
    reveal: (
      <>
        The environment drill ran outside the composition, so neither the address nor the call was recorded. The visibility drill ran inside it, so the request and the response were recorded, but the test asserted the status alone and never read the body. The environment answer is "take the address from the composition"; the visibility answer is "read what the application said".
      </>
    ),
  }}
  learned={[
    'The trace turns a failed run into a list of facts you can act on.',
    'The four questions say where to look; the trace stays silent on the answer that is missing.',
    'One change per run keeps the next trace readable.',
  ]}
  next={[
    {
      label: 'Level 1: One test, one journey',
      to: '/learn/one-test-one-journey/install-and-run',
      note: 'Install ProtoTest, write a test against an in-process application, and read the trace it leaves.',
    },
    {
      label: 'The trace reference',
      to: '/docs/observability/prototrace',
      note: 'Every operation a trace records, and the viewer that draws it.',
    },
  ]}>

## The loop

1. Run the suite.
2. Open the trace at the test that failed.
3. Read the failing check: what it expected, what it read, and which call it judged.
4. Change one thing in the test or the composition.
5. Run again and compare the two traces.

The change is small on purpose. If you change three things and the test passes, the next failure is harder to read.

## Worked example: visibility

The drill sent an empty project name and asserted the status:

```csharp
using var response = await Proto.Context.Rest()
    .Body(new CreateProjectRequest(""))
    .PostAsync("/api/v1/projects");

response.Should.HaveHttpStatus(HttpStatusCode.Created);
```

The check failed with `Expected HTTP status 201 (Created), but received 400 (BadRequest)`. The response body named `validation_failed` and the empty parameter, and the test never looked at it. The trace holds the body as an attachment, so the reader can see the answer in the failed run itself.

The fix asserts what the application said, and the same failure would now name the code:

```csharp
response
    .Should.HaveHttpStatus(HttpStatusCode.BadRequest)
    .Should.MatchShape(new
    {
        code = ProblemCodes.ValidationFailed,
        message = JsonValue.StringContaining("name")
    });
```

Two checks, both recorded, both readable from the trace.

## Worked example: environment

The environment drill is the other direction: the trace says nothing. The test execution span ran about two seconds, recorded no request, and failed with a connection error. Silence in the execution layer is evidence. The call happened outside the run, so nothing wrapped it, and the fix is to call through `Proto.Context.Rest()` like every other journey.

## The four failures as one sentence each

- Time: a real wait does not move the test clock. The fix advances the clock.
- State: the id belonged to no test in the run. The fix creates and reads its own data.
- Environment: the address was hardcoded. The fix takes it from the composition.
- Visibility: the test read the status and ignored the body. The fix asserts the body.

When a failure looks random, walk the four questions in order. A trace usually answers three of them, and the one it stays silent on is the one to fix.

</LearnShell>
