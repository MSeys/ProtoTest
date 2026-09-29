---
id: readiness-instead-of-sleeps
title: Wait for readiness, not for time
sidebar_label: Wait for readiness, not for time
sidebar_position: 2
description: "Register a readiness probe after the application it probes, read the wait in the run layer, and replace a sleep."
---

import LearnShell from '@site/src/components/LearnShell';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import Link from '@docusaurus/Link';

# Wait for readiness, not for time

The run must wait when it starts an address before any test uses it. The choice is between a fixed sleep that sometimes loses and a probe that checks the address and records what it waited.

<LearnShell
  level="Level 3, lesson 2"
  minutes="About 7 minutes"
  outcome={[
    'Register a readiness probe after the piece that publishes the address.',
    'Read the wait in the run layer of a trace.',
    'Say what a sleep would leave in the trace instead.',
  ]}
  before={[
    <>Move the test clock (<Link to="/learn/determinism/the-test-clock">lesson 1</Link>).</>,
    'Nothing installed. The archives are on this site.',
  ]}
  situation={
    <>
      <p>The browser journey needs a real listener: the page is served over HTTP, so the application cannot run in-process for it. The run starts a loopback instance and publishes its address, and something has to wait until that address answers.</p>
      <p>A sleep guesses. On a loaded machine the guess is too short, and on an idle one it wastes the difference. The sample registers a readiness probe instead, and the wait it performs is part of the run's record.</p>
    </>
  }
  checkpoint={{
    question:
      'The API journeys run in-process, so they need no address. Why does the run layer still carry a readiness entity, and what would a sleep for the same wait leave in its place?',
    verify: (
      <>
        Download <a href="pathname:///lessons/l3-clock-window.prototrace">l3-clock-window.prototrace</a>, open it in the{' '}
        <a href="https://trace.prototest.dev">viewer</a>, and read the run screen beside the test screen.
      </>
    ),
    reveal: (
      <>
        The loopback instance binds a port at run time, so nothing knows the address is up until it answers. The probe waits for <code>/health</code> and records its URL, the attempts and the time waited; the entity reads 1 attempt and 113 ms. A sleep would record none of that, only a slower entry in one test's execution span.
      </>
    ),
  }}
  learned={[
    'A run that starts an application registers a readiness probe for its address.',
    'The probe is registered after the piece that publishes the address, because it resolves that address when it runs.',
    'Readiness records the attempts and the time waited; a sleep records nothing.',
  ]}
  next={[
    {
      label: 'Keep state per test and clean it up',
      to: '/learn/determinism/per-test-state-and-cleanup',
      note: 'Provision what the test reads, remove it at teardown, and read both in the trace.',
    },
    {
      label: 'Infrastructure',
      to: '/docs/foundation/infrastructure',
      note: 'How run pieces start, wait and release, readiness included.',
    },
  ]}>

## The registration

The probe sits directly after the listener it probes:

<AnnotatedCode
  filename="Setup.cs"
  code={`if (run.RunsLocalApplications)
{
    // The browser needs a real listener; the page journey follows this instance's address.
    builder.AddLoopbackApplication(NorthstarTargets.Web, NorthstarProgram.CreateApp);
    builder.AddHttpReadiness(NorthstarTargets.Web, "/health");
}`}
  callouts={[
    {line: 1, title: 'Only when the run hosts the application', note: 'A run pointed at a deployed address skips the listener and the probe entirely.'},
    {line: 4, title: 'Start the listener', note: 'The loopback instance binds a free port and publishes the address it got.'},
    {line: 5, title: 'Wait for it to answer', note: 'The probe resolves the application\'s published address and polls /health until it answers. Registering it after the listener is what gives it an address to resolve.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/Setup.cs</code>.</>}
/>

The order is the contract. A probe is awaited at the position it is registered, so a probe registered before the piece that publishes the address has nothing to check. The same shape appears wherever a run starts a real process, including after an AppHost.

## The wait in the run layer

The run layer holds one readiness entity for the loopback instance. From the archive:

| Attribute | Value |
| --- | --- |
| `readiness.url` | `http://127.0.0.1:64464/health`, the port this run bound |
| `readiness.attempts` | `1` |
| `readiness.waitedMs` | `113` |

The address is the run's own, so it changes between runs. The two numbers are the answer a sleep cannot give: the address answered on the first probe, and the wait cost 113 milliseconds. A run that needs several probes adds attempts, and a slow address shows up as a larger number instead of a mystery failure in the first test that used it.

The entity is released with the run, at the same position the listener is released. That release is a `resource.release` entry in the run layer of the same archive.

## What the tests do instead

No test in the sample waits for the application. A test starts, takes its client and calls. The waiting happened once, before the first test, and every test after it reads an address the run already checked. Replace a `Task.Delay` before a request with a readiness probe.

</LearnShell>