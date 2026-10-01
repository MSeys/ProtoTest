---
id: findings
title: Why a passing test can still leave an error
sidebar_label: Findings
sidebar_position: 4
description: "Break a teardown on purpose and follow it into the report as a finding, which explains an outcome without replacing the test result."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import Link from '@docusaurus/Link';

# Why a passing test can still leave an error

<Lesson
  track="Understand failures"
  step="Lesson 4 of 8"
  minutes={7}
  outcomes={[
    'Tell an observation, a finding and an attachment apart',
    'Read the finding a failing teardown leaves',
    'Say why a finding never replaces the test result',
  ]}
  needs={[
    <>The previous lesson, <Link to="/learn/understand-failures/the-trace-as-the-feedback-loop">the trace as a feedback loop</Link></>,
    'The sample cloned, to break a teardown. Reading the report alone also works.',
  ]}
/>

## The problem

A cleanup step throws after the test body passed. The runner prints `Passed: 1`, but the run still fails. You need to know where that error went.

ProtoTest records it as a finding. A finding is something the run reports without changing the result the test reported.

## Do it

### 1. Break a teardown on purpose

Add this file to the sample. It fails after one test:

```csharp
namespace Northstar.ProtoTest;

using global::ProtoTest.Core;

public sealed class FailingTeardownAttribute : ProtoAttribute
{
    public override Task AfterTestAsync(ProtoExecutionContext context)
        => throw new InvalidOperationException("the cleanup step failed on purpose");
}
```

### 2. Run one journey with it

Apply `[FailingTeardown]` to one journey method and run it alone:

```bash
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~ProjectsJourney.CreatingAProjectReturnsIt"
```

Look for a passing test and a failing run:

```text
TearDown failed for test fixture Northstar.ProtoTest.Setup
TearDown : ProtoTest.Core.ProtoRunGateException : Run gate 'no error findings' failed: The run recorded error findings.

Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1
```

The `Passed` line is the test result. The `TearDown` lines come from the next lesson, the run gate.

### 3. Find the finding

Open `TestResults/Northstar.ProtoTest/report.json` under the sample. Look for an item of kind `finding` with category `Teardown` and the message `Teardown failed: the cleanup step failed on purpose`. It sits next to the test result, not in place of it.

## What happened

The lifecycle collects teardown failures instead of throwing at the first one. Each collected failure becomes a finding:

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

A report holds three kinds of evidence:

| Kind | Reading | Who writes it |
| --- | --- | --- |
| Observation | counts or lists what the run saw; it never passes or fails | clients and collectors, through `RecordObservation` |
| Finding | something worth reporting that is deliberately not the test result | a test or the lifecycle, through `AddFinding` |
| Attachment | the exact file the test read | tests and clients, through `context.AddAttachment(...)` and `.CaptureAttachments()` |

Findings land as a report item of kind `finding` and as a finding event in the trace. Attachments land under `resources/<test id>/artifact-N/` inside the archive, with the bytes.

## Check yourself

<Checkpoint
  question="A coverage gap arrives as a Warning with category Coverage. Which of the three kinds is it, and what does it change about the result the test reported?"
  verify={<>With the failing teardown still in place, open <code>report.json</code> and compare the test result with the finding row beside it.</>}>

It is a finding. A finding explains an outcome. It never replaces the result the test reported.

</Checkpoint>

## Remember

- An observation feeds coverage, a finding explains an outcome, an attachment carries bytes.
- A failing teardown becomes an Error finding. The test result stands.
- A Warning finding is recorded and does not fail anything by itself.

## Go deeper

- [Run gates](/learn/understand-failures/run-gates): the next lesson, where the gate turns the finding into a failed run.
- [Contract coverage](/learn/understand-failures/contract-coverage): reads observations.
- [Reporting](/docs/observability/reporting): the JSON and HTML sinks, and the sections every report carries.
