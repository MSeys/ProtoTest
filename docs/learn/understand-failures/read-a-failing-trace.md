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

CI reports a failing check, but its summary may omit the surrounding calls. These traces let you inspect the failed comparison alongside the request and response it judged.

This lesson reads the failures from the last lesson as if they were your own.

## Do it

### 1. Open the two traces of one pair

Each pair compares a deliberate failure with a different test that addresses it. The panes show selected evidence from saved sample traces, rather than the complete operation tree. Their durations describe those recorded executions, not a timing target for your machine.

<TraceDiff />

Pick the Time pair and expand each pane's recorded operations. Each pane links its own archive, which you can open in the [viewer](https://trace.prototest.dev). Select the named test and open Execution to inspect the full sequence.

### 2. Ask four questions of the failed entry

1. **Which check failed?** Find the failed assertion, such as `Assert response shape`, beneath the failed test execution.
2. **What did it expect, and what did it read?** The message carries both. A shape check adds the JSON path: `[$.status]: Values did not match. (Expected: "past_due", Actual: "active")`.
3. **Which call did it judge?** These REST checks are children of the request they judge. Open that parent for its status, duration and attachments.
4. **What differs in the paired test?** Compare its setup, actions and expectations with the failing test's source.

### 3. Compare the pair

For the time pair, the important change is advancing the test clock before reading the organization. The passing test also continues to pay the invoice:

| | The failing test | The test that holds |
| --- | --- | --- |
| Before the read | an invoice is provisioned | an invoice is provisioned, then the clock moves eight days |
| The call | `REST GET /api/v1/organization`, 74.0 ms, HTTP 200 | the same call, 65.3 ms, HTTP 200 |
| The check | shape failed: `$.status` expected `past_due`, read `active` | shape succeeded, then the invoice is paid |

Both requests returned HTTP 200, so the status checks passed. The failing test received `active`, while the passing test received `past_due`. A successful request operation does not mean every assertion on its response succeeded.

### 4. Read a trace that is silent

Open the Environment pair. Its saved failing execution lasted about 2 seconds and contains no request operation. The execution error names the address and connection failure. Setup and teardown still appear elsewhere in the archive.

## What happened

The time failure waited a real second, but that did not advance the test clock. The fix advanced it eight days before making the request. The application evaluated the overdue invoice during that request, and the same status shape check passed. Advancing the clock alone does not execute the application's billing logic.

The environment drill used a plain `HttpClient` with a hardcoded address. It ran inside the test, but bypassed ProtoTest's REST request instrumentation. The trace records the resulting test failure without a separate request operation. The fix uses `Proto.Context.Rest()`, which resolves the configured application and records the request and checks.

An absent operation is a clue, not proof that work ran outside the test. Check the source: the call may have been skipped, failed before recording began, or used an uninstrumented client. Here, the raw client explains the missing entry.

## Check yourself

<Checkpoint
  question='The visibility failure starts with "Expected HTTP status 201 (Created), but received 400 (BadRequest)" and includes the response body. What expectation does the paired test change?'
  verify={<>Read the Visibility pair above, then download <a href="pathname:///lessons/l0-visibility-drill.prototrace">l0-visibility-drill.prototrace</a> and <a href="pathname:///lessons/l0-visibility-fix.prototrace">l0-visibility-fix.prototrace</a> and open both in the <a href="https://trace.prototest.dev">viewer</a>.</>}>

The drill sends an empty project name but expects successful creation. Its status failure already includes the problem body, with `validation_failed` and a message naming the `name` parameter.

The paired test sends the same invalid input and expects HTTP 400. It also checks `code == validation_failed` and that `message` contains `name`. This turns the application's rejection into an explicit test expectation, rather than making a hidden body visible.

</Checkpoint>

## Remember

- Follow a failed REST check to its parent request and inspect the response.
- Compare the paired tests' actions and expectations, not only their durations.
- A missing request entry needs a source check before you decide why it is absent.

## Go deeper

- [The trace as a feedback loop](/learn/understand-failures/the-trace-as-the-feedback-loop): change one thing and compare the two traces.
- [ProtoTrace](/docs/observability/prototrace): what the archive records, and what it cannot see.
