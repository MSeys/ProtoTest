---
id: read-a-failing-trace
title: Read a failing trace
sidebar_label: Read a failing trace
sidebar_position: 1
description: "Read a drill's failure from its trace, name the check that failed, and let the paired test name the fix."
---

import LearnShell from '@site/src/components/LearnShell';
import TraceDiff from '@site/src/components/TraceDiff';
import Link from '@docusaurus/Link';

# Read a failing trace

A failed run is a story with a last page you can read. The four drills in the sample fail on purpose, each next to the test that runs the same journey the right way, so the difference is a habit and the trace shows it.

<LearnShell
  level="Level 4, lesson 1"
  minutes="About 10 minutes"
  outcome={[
    'Read the check that failed and the call it judged.',
    'Name the fix from the failure message and the paired test.',
    'Tell a failure the trace explains from work the trace cannot see.',
  ]}
  before={[
    <>Level 3 (<Link to="/learn/determinism/parallel-safety">parallel safety</Link>).</>,
    'Nothing installed. The archives are on this site.',
  ]}
  situation={
    <>
      <p>The suite in CI reports one failing check. You cannot attach a debugger to that runner, and the log holds a single line. What is left of the run is the trace, and the trace records both halves of the comparison that failed.</p>
      <p>The four pairs below are the same failures from the Level 0 tour, this time read the way you would read a failure of your own. Set <code>ProtoTest__Sample__Drills=true</code> and both halves run and leave their traces.</p>
    </>
  }
  checkpoint={{
    question:
      'The visibility drill failed on "Expected HTTP status 201 (Created), but received 400 (BadRequest)". Its trace still holds the response body. What did the drill fail to do, and what does the fix do instead?',
    verify: (
      <>
        Read the Visibility pair in the diff above, download{' '}
        <a href="pathname:///lessons/l0-visibility-drill.prototrace">l0-visibility-drill.prototrace</a> and{' '}
        <a href="pathname:///lessons/l0-visibility-fix.prototrace">l0-visibility-fix.prototrace</a>, and open both in the{' '}
        <a href="https://trace.prototest.dev">viewer</a>.
      </>
    ),
    reveal: (
      <>
        The drill asserted the status alone and never read the body, so the message it left could only say 400. The application had answered with a problem body naming <code>validation_failed</code> and the empty parameter. The fix asserts that body, so the same failure names the code and the parameter when it happens again.
      </>
    ),
  }}
  learned={[
    'A failed check names what it expected and what it read, and the call it judged sits one entry up.',
    'The drill and the fix run the same journey, so the pair names the fix.',
    'Silence in the execution layer is evidence too: it says the work ran outside the run.',
  ]}
  next={[
    {
      label: 'Contract coverage, not code coverage',
      to: '/learn/evidence/contract-coverage',
      note: 'What the run checked about your API, read from the report it wrote.',
    },
    {
      label: 'ProtoTrace',
      to: '/docs/observability/prototrace',
      note: 'What the archive records, and what it cannot see.',
    },
  ]}>

## The same journey, two runs

Every pair runs one journey twice: the drill fails on purpose, the test beside it holds. The panes carry the records from a recording of the sample with the drills enabled, and each one links the archive a reader can download.

<TraceDiff />

## Read a failed check in four questions

1. **Which check failed?** The failed entry names the assertion, such as `Assert response shape` or `Assert status · 201 Created`.
2. **What did it expect, and what did it read?** The message carries both, and a shape check carries the JSON path: `[$.status]: Values did not match. (Expected: "past_due", Actual: "active")`.
3. **Which call did it judge?** The request sits one entry up, with its status, its duration and its attachments.
4. **What differs in the paired test?** The fix changes one habit: the clock, the data, the address or the assertion.

## One pair, worked through

The time pair is the clearest:

| | The drill | The test that holds |
| --- | --- | --- |
| Before the read | an invoice is provisioned | an invoice is provisioned, then the clock moves eight days |
| The call | `REST GET /api/v1/organization`, 98.1 ms, HTTP 200 | the same call, 66.1 ms, HTTP 200 |
| The check | shape failed: `$.status` expected `past_due`, read `active` | shape succeeded, then the invoice is paid |

The call succeeded in both runs. The application answered quickly, with a subscription that was still `active`, because nothing had moved the clock the application reads. The drill waited a real second, and that changed nothing. The fix advanced the test clock, and the same shape check passed. One failure, four answers, and the pair writes the fix down.

## When the trace is silent

The environment pair is the other direction. Its execution layer holds a single `test.execution` entry of 2.08 s and no request at all, and the failure is a connection error. Nothing wrapped the call, so nothing recorded it. That silence is the diagnosis: the work ran outside the run. The fix takes the address from the composition, and the same call turns into an ordinary request entry.

</LearnShell>