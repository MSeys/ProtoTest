---
id: run-gates
title: Why the run is red when every test is green
sidebar_label: Run gates
sidebar_position: 5
description: "Read the run gate that turns an Error finding into a failed run while the test list stays green."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import Link from '@docusaurus/Link';

# Why the run is red when every test is green

<Lesson
  track="Understand failures"
  step="Lesson 5 of 8"
  minutes={7}
  outcomes={[
    'Read the gate row that a finding feeds',
    'Explain why the test list stays green while the run exits 1',
    'Tell an Error finding from a Warning finding',
  ]}
  needs={[
    <>The previous lesson, <Link to="/learn/understand-failures/findings">findings</Link></>,
    'The sample cloned for the failing teardown step. Reading the report alone also works.',
  ]}
/>

## The problem

Every test passed and the CI job is red. The runner summary says `Passed: 1`, and the process still exits 1.

The difference is a run gate. A run gate is a check over the whole run, not over one test. It runs once after the last test, and a failed gate fails the run.

## Do it

### 1. Find the gate

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

### 2. Run the broken teardown

Add the failing teardown attribute from the findings lesson to one journey and run it alone. Open `TestResults/Northstar.ProtoTest/report.json` and look for these records:

| Record | Reading |
| --- | --- |
| `After · FailingTeardownAttribute`, failed | the teardown step that threw |
| `test.teardown`, failed, error `System.InvalidOperationException` | the phase carries the exception type and message; the body's own result is untouched |
| finding `finding-001`, Error, category `Teardown`, message `Teardown failed: the cleanup step failed on purpose` | the report item, grouped under the test |
| gate `no error findings`, Error, message `The run recorded error findings.` | the verdict the gate returned |
| run event `Gate · no error findings`, outcome `failed` | the run-level trace entry, written once for the whole run |
| runner: Passed 1, exit code 1 | the test passed; the gate failed the run |

This is one recorded run. Timings differ per run, so compare names, states and messages.

### 3. See a finding that does not fail the run

Not every finding fails the run. Open [l4-partial.prototrace](pathname:///lessons/l4-partial.prototrace). Its journey passed its checks but left four response fields unread, and the run recorded:

| Record | Reading |
| --- | --- |
| `test.execution`, partial | the body passed with a warning, so the outcome is partial |
| finding `finding-001`, Warning, category `Coverage`, message `The create response carried 4 fields no assertion mentioned: createdAtUtc, environmentCount, id, slug.` | the report item |
| gate `no error findings`, passed | a Warning is recorded, and this gate only looks for `Error` |

For this test, read the trace for the outcome, not the summary line. It says partial.

## What happened

A test result and a run verdict are two different records. The test result comes from the runner. The run verdict comes from the gates, which read everything the run collected: findings, coverage, resources.

The teardown failure became an Error finding. The test body had passed, so the test result stayed `Passed`. The gate then read the finding after the last test and failed the run. The failure comes out of the run teardown, which is why the output says `TearDown failed for test fixture`.

A cleanup error never hides a failed assertion, and it never becomes one either. It is evidence that the run judges.

## Check yourself

<Checkpoint
  question="The runner prints Passed: 1 and the run exits 1. Which record does the exit code follow, and what did the finding do to the result the test itself reported?"
  verify={<>Compare the runner summary with the finding and gate rows in <code>report.json</code>.</>}>

The exit code follows the failed run gate. `no error findings` reads the report items and fails on an Error finding.

The finding did not change the test result, because the body passed. The teardown failure was recorded beside it, and the gate judged both as run evidence.

</Checkpoint>

## Remember

- A run gate judges the collected evidence after the last test. A failed gate fails the run, and the report is still written.
- The exit code follows the gate verdict, not the test list.
- A Warning finding leaves this gate passing, because it only looks for Error.

## Go deeper

- [Reporting](/docs/observability/reporting): the JSON and HTML sinks, and the sections every report carries.
- [l4-coverage.prototrace](pathname:///lessons/l4-coverage.prototrace): a committed archive where the same gate passes with `No error findings were recorded.`
