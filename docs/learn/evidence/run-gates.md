---
id: run-gates
title: Run gates
sidebar_label: Run gates
sidebar_position: 6
description: "Read the run gate that turns an Error finding into a failed run with a green test list."
---

import LearnShell from '@site/src/components/LearnShell';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import Link from '@docusaurus/Link';

# Run gates

The finding is recorded. Something still has to judge it. This lesson reads the run gate that turns an Error finding into a failed run while the test list stays green.

<LearnShell
  level="Level 4, lesson 6"
  minutes="About 6 minutes"
  outcome={[
    'Read the gate row a finding feeds.',
    'Explain why the test list stays green while the run exits 1.',
    'Tell an Error finding apart from a Warning one.',
  ]}
  before={[
    <>Findings (<Link to="/learn/evidence/findings">lesson 5</Link>).</>,
    'The sample cloned for the failing teardown step; reading the report alone also works.',
  ]}
  situation={
    <>
      <p>A cleanup step threw after the test body passed. The runner summary says <code>Passed: 1</code>, and the process still exits 1.</p>
      <p>The difference is the gate. The sample registers one gate over its findings, and that gate is what fails the run. This lesson walks the path from the finding to the exit code.</p>
    </>
  }
  checkpoint={{
    question:
      'The runner prints Passed: 1 and the run exits 1. Which record does the exit code follow, and what did the finding do to the result the test itself reported?',
    verify: (
      <>
        Add the failing teardown attribute from the findings lesson to one journey, run it alone, and open <code>TestResults/Northstar.ProtoTest/report.json</code> under the sample. Compare the runner summary with the finding and gate rows.
      </>
    ),
    reveal: (
      <>
        The exit code follows the failed run gate: <code>no error findings</code> reads the report items and fails on an Error finding. The finding did not change the test result: the body passed. The teardown failure was recorded beside it, and the gate judged both as run evidence. A cleanup error never hides a failed assertion, and it never becomes one either.
      </>
    ),
  }}
  learned={[
    'A run gate judges the collected evidence after the last test; a failed gate fails the run and the report still exists.',
    'The exit code follows the gate verdict, not the test list.',
    'A Warning finding is recorded and still leaves this gate passing, because the gate only looks for Error.',
  ]}
  next={[
    {
      label: 'Take the evidence to CI',
      to: '/learn/evidence/evidence-in-ci',
      note: 'Keep the archive, post the digest, and run the same suite at three depths.',
    },
    {
      label: 'Reporting',
      to: '/docs/observability/reporting',
      note: 'The JSON and HTML sinks, and the sections every report carries.',
    },
  ]}>

## The run gate reads the report

The sample registers one gate over its findings:

<AnnotatedCode
  filename="Setup.cs"
  code={`.AddRunGate("no error findings", context => context
    .ItemsOfKind(ProtoReportItemKinds.Finding)
    .Any(item => item.Status == ProtoReportStatus.Error)
    ? ProtoRunGateResult.Failed("The run recorded error findings.")
    : ProtoRunGateResult.Passed("No error findings were recorded."));`}
  callouts={[
    {line: 1, title: 'A name and a delegate', note: 'The name becomes the row in the report and the message in the failure; a class implementing IProtoRunGate works the same way.'},
    {line: 2, title: 'It reads items, not tests', note: 'ItemsOfKind selects the findings the run collected, wherever they came from. A teardown finding counts like any other.'},
    {line: 4, title: 'Failed fails the run', note: 'A Failed verdict throws ProtoRunGateException out of the run teardown. Warning and Skipped are recorded without failing.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/Setup.cs</code>. Gates run once after the last test and before the reports are written, so a failed gate still produces its report.</>}
/>

## The records a failed run leaves

Run the broken teardown once more and the same story appears in the archive and the report.

This is one such run. Timings differ per run. Compare names, states, messages.

| Record | Reading |
| --- | --- |
| `After · FailingTeardownAttribute`, failed, 0.6 ms | the teardown step that threw |
| `test.teardown`, failed, 30.3 ms, error `System.InvalidOperationException` | the phase carries the type and the message; the body's own result is untouched |
| finding `finding-001`, Error, category `Teardown`, message `Teardown failed: the cleanup step failed on purpose`, tag `InvalidOperationException` | the report item, grouped under the test |
| gate `no error findings`, Error, message `The run recorded error findings.` | the verdict the sample's gate returned |
| run event `Gate · no error findings`, outcome `failed` | the run-level trace entry, written once for the whole run |
| report summary: Findings 1, Errors 2 | the finding and the gate are the two Error items |
| runner: Passed 1, exit code 1 | the test passed; the gate failed the run |

The same gate row passes in every committed archive.

## A warning finding leaves the gate passing

Not every finding fails the run. <a href="pathname:///lessons/l4-partial.prototrace">l4-partial.prototrace</a> holds a journey that passed its checks but named the response fields it left unread. The run recorded both halves:

| Record | Reading |
| --- | --- |
| `test.execution`, partial, 172.5 ms | the body passed with a warning, so the test outcome is partial |
| finding `finding-001`, Warning, category `Coverage`, message `The create response carried 4 fields no assertion mentioned: createdAtUtc, environmentCount, id, slug.` | the report item, grouped under the test |
| gate `no error findings`, passed, message `No error findings were recorded.` | the verdict: a Warning is recorded and still leaves this gate passing, because the gate only looks for `Error` |
| report summary: Findings 1, Warnings 1, Errors 0 | the finding is the one Warning item |

The trace records partial for this test. Read the trace for the outcome, not the summary line.

## Going further

See the passing row in a committed archive: <a href="pathname:///lessons/l4-coverage.prototrace">l4-coverage.prototrace</a> carries `no error findings` with `No error findings were recorded.`

</LearnShell>
