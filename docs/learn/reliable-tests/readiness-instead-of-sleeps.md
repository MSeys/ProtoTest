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
  step="Lesson 2 of 4"
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

On a loaded build agent, three seconds may be too short. On a fast laptop, the service may already be ready.
The sleep adds elapsed time but records no evidence that the service can answer a request.

Replace the fixed delay with a readiness probe: a check that retries until its condition passes or the wait ends.
The trace records the attempts and elapsed time when the probe succeeds.

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
- `AddLoopbackApplication` binds a free port and publishes the address, unless configuration already supplies an address for that application.
- `AddHttpReadiness` resolves the address and polls `/health` until it receives an HTTP response.

The default probe accepts any HTTP response, including a 404 or 500. It proves the endpoint responds, without requiring a successful health status.
To require a 2xx response, pass `ready: response => response.IsSuccessStatusCode` to `AddHttpReadiness`.

The host starts what the setup class registered in that order. So register the probe after whatever gives the application its address, here the loopback application.
If no address is available at the probe's turn, it skips and records `readiness.skipped`.
An address supplied through configuration is already available, so it does not depend on a publisher starting first.

### 2. Write tests that do not wait

The host runs this probe once during startup, before the first test. The browser journey then uses the loopback address without adding its own startup delay.
The tests that call the API directly use the in-process server instead, the application running inside the test process, so this probe does not check them.

If the probe cannot satisfy its condition before the wait times out, host startup fails with the probe name and attempt count.
Cancelling startup also cancels the wait. The configured timeout is checked between attempts, so an in-flight HTTP request can extend the elapsed time.

Passing startup readiness does not guarantee that the application stays healthy throughout the tests.

### 3. Read the wait in the trace

Download [l3-clock-window.prototrace](pathname:///lessons/l3-clock-window.prototrace) and open it in the
[viewer](https://trace.prototest.dev). Open the run screen, not the test screen. The run layer holds one
readiness entity for the loopback instance:

| Attribute | Value |
| --- | --- |
| `readiness.url` | `http://127.0.0.1:54120/health`, the port this run bound |
| `readiness.attempts` | `1` |
| `readiness.waitedMs` | `85` |

The operating system chooses an available port, so your run may use a different one. The probe's entry in the trace is released when the run ends, like the other pieces the host owns.
Its `resource.release` entry appears in the same layer.

## What happened

The endpoint answered on the first attempt, and the trace recorded 85 milliseconds of waiting.
That evidence distinguishes a successful readiness check from a fixed delay that never checks the service.
An endpoint that initially refuses connections can require more attempts. One slow response can also increase the wait without increasing the attempt count.

The host did this waiting during startup. The wait still contributes to the run's total time, but each test does not repeat it.

## Check yourself

<Checkpoint
  question="The API journeys use an in-process test server without a network listener. Why does the run layer still carry a readiness entity?"
  verify={<>Read the run screen beside the test screen in <a href="pathname:///lessons/l3-clock-window.prototrace">l3-clock-window.prototrace</a>.</>}>

The browser journey needs the separate loopback listener. Its probe requests `/health` and records the URL, attempt count and elapsed time.
The in-process API clients still use logical HTTP addresses, but their requests pass through the test server's handler without a network listener.
A sleep adds time without recording whether either application is ready.

</Checkpoint>

## Remember

- Use a readiness condition to decide when tests can start calling a network service.
- Register the probe after the piece that publishes the address.
- A successful probe records attempts and time waited. A sleep adds time without checking readiness.

## Go deeper

- [Infrastructure](/docs/foundation/infrastructure): how run pieces start, wait and release, readiness included.
- Next lesson: [Parallel safety](/learn/reliable-tests/parallel-safety).
