---
id: the-four-questions
title: The four questions
sidebar_label: The four questions
sidebar_position: 1
description: "Why integration tests get hard: time, state, environment and visibility, and where each one shows up in a trace."
---

import LearnShell from '@site/src/components/LearnShell';

# The four questions

An integration test talks to the parts of a system that actually run: an API, a database, a broker, a browser, a clock you do not own. That is what makes it valuable, and it is why it fails in ways a unit test never does.

Before you can trust a test like that, it has to answer four questions.

<LearnShell
  level="Level 0, lesson 1"
  minutes="About 8 minutes"
  outcome={[
    'Name the four questions an integration test has to answer.',
    'Point at the part of a trace that answers each one.',
    'Say what a passing test leaves behind.',
  ]}
  before={[
    'Nothing from this track.',
    <>The archives this level reads are on this site, so no install is needed. To run the sample as well, <code>dotnet test samples/Northstar.ProtoTest</code> needs the .NET SDK and the repository.</>,
  ]}
  situation={
    <>
      <p>An integration test fails once, passes on the retry, and the failure message says nothing you can act on. Most integration failures look like this. Something outside the tested code changed, and the test never named what it depended on.</p>
      <p>The four questions name those somethings. The rest of this level answers each one with a test that fails on purpose and the test that fixes it.</p>
    </>
  }
  checkpoint={{
    question:
      'The time drill waits one real second and the application still reports the organization as active. Why does the wait not close the due window?',
    verify: (
      <>
        Download <a href="pathname:///lessons/l0-time-drill.prototrace">l0-time-drill.prototrace</a>, drop it on the{' '}
        <a href="https://trace.prototest.dev">viewer</a>, and follow the failed check down to the values it printed. When you can explain the failure without running it again, you have the habit the rest of the track builds on.
      </>
    ),
    reveal: (
      <>
        The application runs on the test clock, and real time does not move it. The due window stays open until something advances that clock. The fix moves it from the test side with <code>Proto.Context.Clock.Advance</code>, so the application and the assertion read the same moment.
      </>
    ),
  }}
  learned={[
    'Time, state, environment and visibility are the four things an integration test has to get right.',
    'Each one has a concrete answer in the composition or in the trace.',
    'A passing test answers all four at once; a failure is usually one missing answer.',
  ]}
  next={[
    {
      label: 'A failure tour',
      to: '/learn/why-integration-tests-get-hard/a-failure-tour',
      note: 'Four deliberate failures, each next to the test that does the same journey the right way.',
    },
    {
      label: 'The trace reference',
      to: '/docs/observability/prototrace',
      note: 'What a .prototrace records, and how to read it.',
    },
  ]}>

## The four questions

The sample suite answers each question twice: once the way that fails, once the way that holds. This is what the failing half recorded.

| Question | The drill | What the trace recorded |
| --- | --- | --- |
| Time | `ARealWaitDoesNotCloseTheDueWindow` waits one real second | the shape check failed on `$.status`: expected `past_due`, read `active` |
| State | `AnUnknownProjectIdIsTreatedAsMine` reads the project id `prj_1` | the request returned 404; no test in the run created that id |
| Environment | `TheAddressWasHardcodedForOneMachine` opens a raw client on `127.0.0.1:5099` | the test ran about two seconds and recorded no request at all |
| Visibility | `ABareStatusHidesWhatTheApplicationSaid` sends an empty project name | the status check saw 400; the body that named `validation_failed` stayed unread |

### Time: who moves the clock?

The application computes every stamp and billing period from a time provider. The test host hands it the test's clock, so real time does not move anything the application can see. A wait therefore changes nothing. The fix, `TheTestClockClosesTheDueWindow`, calls `Proto.Context.Clock.Advance(TimeSpan.FromDays(8))` and then reads the same organization. One clock, moved from the test side.

### State: what does the test share?

A test that reads `prj_1` reads a record some other run created, or no record at all. The fix, `EachTenantSeesOnlyItsOwnProjects`, creates its own project, lists the projects its tenant can see, and finds exactly one. Data that belongs to one test stays in that test, and it is removed when the test ends.

### Environment: where does the address come from?

A raw `HttpClient` with a fixed address talks to one machine: the one where it was written. It is also outside the run, so the trace cannot see the call. The fix, `TheAddressComesFromTheComposition`, calls the same endpoint through `Proto.Context.Rest()`, which takes its address from the run. The same test then works in-process, in a container, or against a published environment.

### Visibility: what can the test show when it fails?

A test that asserts the status alone throws away what the application said. The drill sent an empty project name, expected `201 Created`, and the check reported `400`. The body named `validation_failed` and the empty parameter, and nothing read it. The fix, `TheProblemBodyNamesTheCodeAndDetail`, asserts the problem body, so the same failure would name the code and the message.

A passing test answers all four: it moves the clock, creates and removes its own data, takes the address from the composition, and asserts something that names the difference when it fails. The rest of this level reads the four drill pairs in full.

</LearnShell>
