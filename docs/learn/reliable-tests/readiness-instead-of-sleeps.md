---
id: readiness-instead-of-sleeps
title: Wait for readiness, not for time
sidebar_label: Wait for readiness, not for time
sidebar_position: 2
description: "Replace a sleep before the first request with a readiness probe, and read the wait in the run layer of a trace."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';

# Wait for readiness, not for time

<Lesson
  track="Reliable tests"
  step="Lesson 2 of 3"
  minutes={6}
  outcomes={[
    'Replace a sleep with a readiness probe',
    'Register the probe after the piece that publishes the address',
    'Read the wait in the run layer of a trace',
  ]}
  needs={[<>The previous lesson, <a href="/learn/reliable-tests/the-test-clock">Move the test clock</a></>]}
/>

## The problem

Your test starts a service and calls it right away. Sometimes the service is not listening yet, and the first
request fails. So someone adds a sleep.

```csharp
// A flaky version: it guesses how long the service needs.
await Task.Delay(TimeSpan.FromSeconds(3));
```

On a loaded build agent three seconds is too short, and the test fails. On a fast laptop three seconds is wasted
on every run. Either way the sleep records nothing, so you cannot tell which case you are in.

The fix is to ask the service instead of guessing. A readiness probe polls an address until it answers, and the
run records what it waited.

## Do it

### 1. Start the listener, then register the probe

The sample's browser journey needs a real listener, because the page is served over HTTP. The run starts a
loopback instance, which is a copy of the application bound to a free port. The probe goes directly after it:

```csharp
if (run.RunsLocalApplications)
{
    // The browser needs a real listener; the page journey follows this instance's address.
    builder.AddLoopbackApplication(NorthstarTargets.Web, NorthstarProgram.CreateApp);
    builder.AddHttpReadiness(NorthstarTargets.Web, "/health");
}
```

The code is in `samples/Northstar.ProtoTest/Setup.cs`. Three things to notice:

- The `if` means a run pointed at a deployed address skips both the listener and the probe.
- `AddLoopbackApplication` binds a free port and publishes the address it got.
- `AddHttpReadiness` resolves that published address and polls `/health` until it answers.

The order matters. The run waits on the probe at the position where it is registered. A probe registered before
the listener has no address to check. The same order applies wherever a run starts a real process, including after
an AppHost.

### 2. Write tests that do not wait

A test in the sample starts, takes its client and calls. It never sleeps for the application, because the wait
happened once, before the first test. Every test reads an address the run already checked.

### 3. Read the wait in the trace

Download [l3-clock-window.prototrace](pathname:///lessons/l3-clock-window.prototrace) and open it in the
[viewer](https://trace.prototest.dev). Open the run screen, not the test screen. The run layer holds one
readiness entity for the loopback instance:

| Attribute | Value |
| --- | --- |
| `readiness.url` | `http://127.0.0.1:54120/health`, the port this run bound |
| `readiness.attempts` | `1` |
| `readiness.waitedMs` | `85` |

The port differs on every run. The entity is released with the run, shown as a `resource.release` entry in the
same layer.

## What happened

The probe answered on its first attempt, and the wait cost 85 milliseconds. A sleep could not tell you either
number. A slower address would show up as more attempts and a larger wait, instead of a mystery failure in the
first test that used it.

The run did the waiting once, and the probe stopped as soon as the address answered. No test paid for it.

## Check yourself

<Checkpoint
  question="The API journeys run in-process, so they need no address. Why does the run layer still carry a readiness entity?"
  verify={<>Read the run screen beside the test screen in <a href="pathname:///lessons/l3-clock-window.prototrace">l3-clock-window.prototrace</a>.</>}>

The loopback instance binds a port at run time, so nothing knows the address is up until it answers. The probe
waits for `/health` and records its URL, the attempts and the time waited. A sleep would record none of that,
only a slower entry in one test's execution span.

</Checkpoint>

## Remember

- A run that starts an application registers a readiness probe for its address.
- Register the probe after the piece that publishes the address.
- Readiness records attempts and time waited. A sleep records nothing.

## Go deeper

- [Infrastructure](/docs/foundation/infrastructure): how run pieces start, wait and release, readiness included.
- Next lesson: [Parallel safety](/learn/reliable-tests/parallel-safety).
