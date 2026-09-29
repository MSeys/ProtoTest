---
id: read-the-findings-and-the-run-gate
title: Read the findings and the run gate
sidebar_label: Findings and the run gate
sidebar_position: 4
description: "Follow a teardown failure into the report as a finding, and read the run gate that turns it into a failed run with a green test list."
---

import LearnShell from '@site/src/components/LearnShell';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import Link from '@docusaurus/Link';

# Read the findings and the run gate

All tests can pass while the run still fails. This lesson follows a teardown failure into the report as a finding, and reads the run gate that turns it into a failed run.

<LearnShell
  level="Level 4, lesson 4"
  minutes="About 10 minutes"
  outcome={[
    'Tell an observation, a finding and an attachment apart.',
    'Read a finding a failing teardown left and the gate row it feeds.',
    'Explain why the test list stays green while the run exits 1.',
  ]}
  before={[
    <>The archive and the reports (<Link to="/learn/evidence/artifacts-and-reports">lesson 3</Link>).</>,
    'The sample cloned for the failing teardown step; reading the report alone also works.',
  ]}
  situation={
    <>
      <p>A cleanup step throws after the test body passed. The runner's summary says <code>Passed: 1</code>, and the process still exits 1. The difference is one finding and the gate that reads it.</p>
      <p>A finding is evidence the run collected, not a test failure. The sample registers one gate over its findings, and that gate is what fails the run. This lesson walks the path from the attribute that throws to the exit code.</p>
    </>
  }
  checkpoint={{
    question:
      'The runner prints Passed: 1 and the run exits 1. Which record does the exit code follow, and what did the finding do to the result the test itself reported?',
    verify: (
      <>
        Add the failing teardown attribute below to one journey, run it alone, and open <code>TestResults/Northstar.ProtoTest/report.json</code> under the sample. Compare the runner summary with the finding and gate rows.
      </>
    ),
    reveal: (
      <>
        The exit code follows the failed run gate: <code>no error findings</code> reads the report items and fails on an Error finding. The finding did not change the test's result: the body passed, the teardown failure was recorded beside it, and the gate judged both as run evidence. A cleanup error never hides a failed assertion, and it never becomes one either.
      </>
    ),
  }}
  learned={[
    'An observation feeds coverage, a finding explains an outcome, an attachment carries bytes.',
    'A teardown failure is recorded as an Error finding without replacing the result the test reported.',
    'A run gate judges the collected evidence after the last test; a failed gate fails the run and the report still exists.',
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

## Three kinds of evidence

The report holds more than results, and the three kinds are worth telling apart before the gate reads them:

| Kind | Who writes it | Where it lands |
| --- | --- | --- |
| Observation | clients and collectors, through `RecordObservation` | coverage and traffic rows in the report, operations in the trace |
| Finding | a test or the lifecycle, through `AddFinding` | a report item with kind `finding`, plus a finding event in the trace |
| Attachment | tests and clients, through `context.AddAttachment(...)` and `.CaptureAttachments()` | `resources/&lt;test id&gt;/artifact-N/...` inside the archive, with the bytes |

An observation never passes or fails; it counts or lists what the run saw. An attachment is the exact file the test read. A finding is the middle kind: something worth reporting that is deliberately not the test's own result. The [contract coverage lesson](/learn/evidence/contract-coverage) reads observations, and the archive lesson opens attachments.

## A teardown failure becomes a finding

The lifecycle collects teardown failures instead of throwing at the first one, and every collected failure becomes a finding:

<AnnotatedCode
  filename="ProtoTestLifecycle.cs"
  code={`// The teardown exception is evidence, not a replacement for the result the test reported:
// the original outcome stands, so a failed assertion is not hidden by a cleanup error.
lifecycleOperation.Fail(exceptions[^1]);
foreach (var failure in exceptions.Skip(exceptionCountBeforeTeardown))
{
    // The same path as any other finding, so a teardown failure reaches the sinks and run
    // gates instead of living only in the trace.
    context.AddFinding(
        $"Teardown failed: {failure.Message}",
        ProtoReportStatus.Error,
        category: "Teardown",
        targetName: context.TestName,
        tags: [failure.GetType().Name]);
}`}
  callouts={[
    {line: 1, title: 'The result stands', note: 'The teardown exception is evidence, not a replacement: the passing body stays a passed test.'},
    {line: 3, title: 'The phase records the failure', note: 'The teardown operation fails with the exception, so the trace shows which step threw.'},
    {line: 9, title: 'The finding carries the rest', note: 'Status Error, category Teardown, the test as its target and the exception type as a tag. It reaches every sink and every run gate.'},
  ]}
  foot={<>From <code>src/ProtoTest.Core/Internal/ProtoTestLifecycle.cs</code>. The collect-mode flow is why one failing step does not stop the others.</>}
/>

## Break a teardown on purpose

Add a file to the sample that fails after one test:

```csharp
namespace Northstar.ProtoTest;

using global::ProtoTest.Core;

public sealed class FailingTeardownAttribute : ProtoAttribute
{
    public override Task AfterTestAsync(ProtoExecutionContext context)
        => throw new InvalidOperationException("the cleanup step failed on purpose");
}
```

Apply `[FailingTeardown]` to one journey method and run it alone:

```bash
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~ProjectsJourney.CreatingAProjectReturnsIt"
```

The body passes and the run still exits 1:

```text
TearDown failed for test fixture Northstar.ProtoTest.Setup
TearDown : ProtoTest.Core.ProtoRunGateException : Run gate 'no error findings' failed: The run recorded error findings.

Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1
```

The `Passed` line is the framework's view of the test. The `TearDown` lines come from the run gate when the run stops.

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

Run the broken teardown once more and the same story appears in the archive and the report. This is one such run:

| Record | Reading |
| --- | --- |
| `After · FailingTeardownAttribute`, failed, 0.6 ms | the teardown step that threw |
| `test.teardown`, failed, 30.3 ms, error `System.InvalidOperationException` | the phase carries the type and the message; the body's own result is untouched |
| finding `finding-001`, Error, category `Teardown`, message `Teardown failed: the cleanup step failed on purpose`, tag `InvalidOperationException` | the report item, grouped under the test |
| gate `no error findings`, Error, message `The run recorded error findings.` | the verdict the sample's gate returned |
| run event `Gate · no error findings`, outcome `failed` | the run-level trace entry, written once for the whole run |
| report summary: Findings 1, Errors 2 | the finding and the gate are the two Error items |
| runner: Passed 1, exit code 1 | the test passed; the gate failed the run |

The same gate row passes in every committed archive. <a href="pathname:///lessons/l4-coverage.prototrace">l4-coverage.prototrace</a> carries `no error findings` with `No error findings were recorded.`

## A warning finding leaves the gate passing

Not every finding fails the run. <a href="pathname:///lessons/l4-partial.prototrace">l4-partial.prototrace</a> holds a journey that passed its checks but named the response fields it left unread. The run recorded both halves:

| Record | Reading |
| --- | --- |
| `test.execution`, partial, 172.5 ms | the body passed with a warning, so the test outcome is partial |
| finding `finding-001`, Warning, category `Coverage`, message `The create response carried 4 fields no assertion mentioned: createdAtUtc, environmentCount, id, slug.` | the report item, grouped under the test |
| gate `no error findings`, passed, message `No error findings were recorded.` | the verdict: a Warning is recorded and still leaves this gate passing, because the gate only looks for `Error` |
| report summary: Findings 1, Warnings 1, Errors 0 | the finding is the one Warning item |

The runner prints this test as skipped: NUnit has no partial status, so a warning reads as a skip in the summary while the trace records partial. Read the trace for the outcome, not the summary line.

</LearnShell>
