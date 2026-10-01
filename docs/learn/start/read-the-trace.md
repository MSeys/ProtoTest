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

A test fails on a build server. You cannot attach a debugger there, and the log is one line long. The test has already finished, so the only witness left is the file the run wrote.

That file is the trace. This lesson makes your own test fail, then finds the cause without running it again.

## Do it

### 1. Break the test on purpose

In `MyFirstJourney.cs`, change the shape check so it expects a different name:

```csharp
.Should.MatchShape(new { name = "someone-else", status = ProjectStatuses.Active });
```

Run it:

```bash
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~MyFirstJourney"
```

The test fails. The runner prints a message that names the property and both values:

```
[$.name]: Values did not match. (Expected: "someone-else", Actual: "first-...")
```

Already this is a good failure. It says which field differs, what the test expected and what the application sent. The rest of the lesson shows where the same facts live in the trace.

### 2. Open the trace in the viewer

Find the new file under `bin/Debug/net8.0/TestResults/`. It is named `prototest-{runId}.prototrace`, as in lesson 1. Open the [viewer](https://trace.prototest.dev) and drop the file on it.

The viewer lists the run's tests. Open `CreatingAProjectReturnsIt`, the failed one.

No run at hand? Download [l0-time-drill.prototrace](pathname:///lessons/l0-time-drill.prototrace), a recorded failure from the sample, and follow the same steps.

### 3. Find the failing check

A test in the trace is a list of operations. An operation is one recorded step: a request, a data setup, or a check. A check is an assertion, as the trace records it.

In the test's steps, find the one marked failed. It is the shape check, `Assert response shape`. The request above it succeeded, so the application answered and the check judged the answer.

### 4. Read what differed

Select the failed check. The viewer shows the property, `$.name`, with the value the test expected and the value the application returned. These are the same values as in the runner message.

In the recorded time drill, the same step reads: `[$.status]: Values did not match. (Expected: "past_due", Actual: "active")`.

### 5. Jump to the line

On the same check, open the Source block. It shows the code around the line where the check started, with that line marked. In your test, that is the line where the check statement starts. In the recorded time drill, it is line 36 of `FailureDrills.cs`.

You now have the failing check, the values and the line, without a rerun.

### 6. Put the test back

Restore `name` in the shape check and run the filtered test again. It passes.

## What happened

The trace holds every operation the test ran, in order, and each check records what it compared. A failed check is more than a red mark. It keeps the expected value, the actual value and the place in your code.

That is why the trace answers the three questions of any failure: which check failed, what differed, and where in the code. Reading a trace is the same three steps every time.

## Check yourself

<Checkpoint
  question="In the recorded time drill the request returned 200 OK, yet the test failed. Which operation failed, and what did it compare?"
  verify={<>Open <a href="pathname:///lessons/l0-time-drill.prototrace">l0-time-drill.prototrace</a> in the <a href="https://trace.prototest.dev">viewer</a>, select the failed step and read its values.</>}
>

The status check passed, and the shape check failed. It compared the field `$.status`: the test expected `"past_due"` and the application returned `"active"`. A passing request does not mean a passing test. The check that failed is the one to read, and the Source block shows it started on line 36 of `FailureDrills.cs`.

</Checkpoint>

## Remember

- A trace is a list of operations. A check is one of them, and a failed one keeps both values.
- Read a failure in three steps: the failing check, the values that differed, the line of code.
- Reports tell you that a test failed. The trace tells you why.

## Go deeper

- [Read a failing trace](/learn/understand-failures/read-a-failing-trace): four failures, each next to the fix that holds.
- [ProtoTrace](/docs/observability/prototrace): every kind of operation, the layers of a trace (run, setup, execution, teardown), and what a trace cannot see.
- Next track: [Good tests](/learn/good-tests/capabilities-and-the-host) starts with what a capability is and how a run adds one.
