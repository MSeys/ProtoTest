---
id: read-the-trace
title: See why a test failed
sidebar_label: Read the trace
sidebar_position: 3
description: "Break your first test on purpose, open its trace in the viewer, and find the failing check, the values that differed and the line of code."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';
import Link from '@docusaurus/Link';

# See why a test failed

<Lesson
  track="Start"
  step="Lesson 3 of 3"
  minutes={10}
  outcomes={[
    'Open a trace in the viewer and find the check that failed.',
    'Read the expected and actual values.',
    'Jump from the failed check to the line of code.',
  ]}
  needs={[
    <><Link to="/learn/start/write-your-first-test">Write your first test</Link>, with <code>MyFirstJourney.cs</code> in the sample.</>,
  ]}
/>

## The problem

A test fails on a build server, where you cannot attach a debugger. You need to find which check failed, what differed and where it happened.

The trace keeps the evidence from the run. In this lesson, you make your test fail and use its trace to find the cause without running it again.

## Do it

### 1. Break the test on purpose

In `MyFirstJourney.cs`, change the shape check so it expects a different name:

```csharp
.Should.MatchShape(new { name = "someone-else", status = ProjectStatuses.Active });
```

Run it from the repository root:

```bash
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~MyFirstJourney"
```

The test fails. Its error message includes the property and both values. Here, `first-...` abbreviates the generated project name:

```
[$.name]: Values did not match. (Expected: "someone-else", Actual: "first-...")
```

The message says which field differs, what the test expected and what the application sent. You can find the same facts in the trace.

### 2. Open the trace in the viewer

In `samples/Northstar.ProtoTest/`, find the newest file under `bin/Debug/net8.0/TestResults/`. It is named `prototest-{runId}.prototrace`, as in lesson 1. Open the [viewer](https://trace.prototest.dev) and drop the file on it.

The viewer lists the run's tests. Open `CreatingAProjectReturnsIt`, the failed one.

If you do not have a trace, download [l0-time-drill.prototrace](pathname:///lessons/l0-time-drill.prototrace), a recorded failure from the sample. Open its failed test, `ARealWaitDoesNotCloseTheDueWindow`, and continue from step 3. It compares a status instead of a name.

### 3. Find the failing check

The viewer shows the test as a list of operations. An operation is one recorded step, such as a request, a data setup step or a check. A check is an assertion recorded in the trace.

Find the failed check named `Assert response shape`. The request and the HTTP status check succeeded. The shape check failed because a field in the response differed from the expected value.

### 4. Read what differed

Select the failed check. The viewer shows the property, `$.name`, with the value the test expected and the value the application returned. These are the same values as in the runner message.

If you opened the recorded time drill, its shape check reads: `[$.status]: Values did not match. (Expected: "past_due", Actual: "active")`.

### 5. Jump to the line

On the same check, open the Source block. It shows the code around the line where the check statement starts, with that line marked. In your test, the statement starts with `created`. In the recorded time drill, it starts on line 36 of `FailureDrills.cs`.

You now have the failing check, the values and the line, without a rerun.

### 6. Put the test back

If you changed your test, replace `name = "someone-else"` with `name` in the shape check. Run the filtered test again. It passes.

## What happened

The trace recorded the request and its checks. The failed shape check kept the expected value, the actual value and the statement's location.

You used those details to find which field differed and where the test checked it. The trace only shows work that ProtoTest recorded. Other failures may need different evidence.

## Check yourself

<Checkpoint
  question="In the recorded time drill the request returned 200 OK, yet the test failed. Which operation failed, and what did it compare?"
  verify={<>Open <a href="pathname:///lessons/l0-time-drill.prototrace">l0-time-drill.prototrace</a> in the <a href="https://trace.prototest.dev">viewer</a>, select the failed step and read its values.</>}
>

The status check passed, and the shape check failed. It compared the response field `$.status`: the test expected `"past_due"` and the application returned `"active"`. An HTTP 200 response does not guarantee that the response body is correct. The Source block points to the check statement on line 36 of `FailureDrills.cs`.

</Checkpoint>

## Remember

- A trace contains recorded operations. The failed shape check in this lesson keeps the expected and actual values.
- Read a failure in three steps: the failing check, the values that differed, the line of code.
- Open the trace to inspect the failed check and its evidence.

## Go deeper

- [Read a failing trace](/learn/understand-failures/read-a-failing-trace): four failures, each paired with its fix.
- [ProtoTrace](/docs/observability/prototrace): every kind of operation, the layers of a trace (run, setup, execution, teardown), and what a trace cannot see.
- Next track: [Good tests](/learn/good-tests/capabilities-and-the-host) starts with what a capability is and how a run adds one.
