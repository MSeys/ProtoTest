---
id: read-a-failing-trace
title: Read a failing trace
sidebar_label: Read a failing trace
sidebar_position: 2
description: "Open one failing trace in the viewer, name the check that failed and the call it judged, and let the paired test name the fix."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';
import TraceDiff from '@site/src/components/TraceDiff';
import Link from '@docusaurus/Link';

# Read a failing trace

<Lesson
  track="Understand failures"
  step="Lesson 2 of 8"
  minutes={9}
  outcomes={[
    'Find the check that failed and the call it judged',
    'Compare a failing trace with a passing one to find the fix',
    'Tell a missing operation apart from a failed one',
  ]}
  needs={[
    <>The previous lesson, <Link to="/learn/understand-failures/a-failure-tour">run four failures on purpose</Link></>,
    'Nothing installed. The archives are on this site.',
  ]}
/>

## The problem

CI reports one failing check. You cannot attach a debugger to that runner, and the log holds a single line. What is left of the run is the trace, and it holds both sides of the comparison that failed.

This lesson reads the failures from the last lesson as if they were your own.

## Do it

### 1. Open the two traces of one pair

Each pair below is one journey run twice. The left pane is the failing test, the right pane the test that holds. Every name, duration and message is from a recording of the sample with the failing tests enabled.

<TraceDiff />

Pick the Time pair. Each pane links its own archive, which you can open in the [viewer](https://trace.prototest.dev).

### 2. Ask four questions of the failed entry

1. **Which check failed?** The failed entry names the assertion, such as `Assert response shape`.
2. **What did it expect, and what did it read?** The message carries both. A shape check adds the JSON path: `[$.status]: Values did not match. (Expected: "past_due", Actual: "active")`.
3. **Which call did it judge?** The request sits one entry up, with its status, its duration and its attachments.
4. **What differs in the paired test?** The fix changes one habit.

### 3. Compare the pair

For the time pair, the two traces differ in one row.

| | The failing test | The test that holds |
| --- | --- | --- |
| Before the read | an invoice is provisioned | an invoice is provisioned, then the clock moves eight days |
| The call | `REST GET /api/v1/organization`, 74.0 ms, HTTP 200 | the same call, 65.3 ms, HTTP 200 |
| The check | shape failed: `$.status` expected `past_due`, read `active` | shape succeeded, then the invoice is paid |

Look at the call first. It succeeded in both runs. The application answered quickly with a subscription that was still `active`.

### 4. Read a trace that is silent

Open the Environment pair. The failing side holds one entry of about 2 seconds and no request at all. It records a connection error and nothing about the call.

## What happened

The time failure waited a real second. That changed nothing, because the application reads the test clock and nothing had moved it. The fix advanced the test clock, and the same shape check passed. The pair wrote the fix down.

The environment failure is the other direction. The test used a raw client that ran outside the run, so the run never wrapped it. The missing request is the diagnosis. The fix takes the address from the run, and the same call turns into an ordinary request entry.

So a trace answers in two ways. A failed check names what was read. An absent operation says the work ran outside the run.

## Check yourself

<Checkpoint
  question='The visibility failure ended on "Expected HTTP status 201 (Created), but received 400 (BadRequest)". Its trace still holds the response body. What did the test fail to do, and what does the fix do instead?'
  verify={<>Read the Visibility pair above, then download <a href="pathname:///lessons/l0-visibility-drill.prototrace">l0-visibility-drill.prototrace</a> and <a href="pathname:///lessons/l0-visibility-fix.prototrace">l0-visibility-fix.prototrace</a> and open both in the <a href="https://trace.prototest.dev">viewer</a>.</>}>

The test asserted the status alone and never read the body, so its message could only say 400. The application had answered with a problem body that names `validation_failed` and the empty parameter.

The fix asserts that body, so the same failure names the code and the parameter when it happens again.

</Checkpoint>

## Remember

- A failed check names what it expected and what it read. The call it judged sits one entry up.
- The failing and passing tests run the same journey, so the pair names the fix.
- Silence is evidence too: no request means the work ran outside the run.

## Go deeper

- [The trace as a feedback loop](/learn/understand-failures/the-trace-as-the-feedback-loop): change one thing and compare the two traces.
- [ProtoTrace](/docs/observability/prototrace): what the archive records, and what it cannot see.
