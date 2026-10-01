---
id: findings
title: Findings
sidebar_label: Findings
sidebar_position: 5
description: "Tell observations, findings and attachments apart, and follow a teardown failure into the report as a finding."
---

import LearnShell from '@site/src/components/LearnShell';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import Link from '@docusaurus/Link';

# Findings

All tests can pass while the run still fails. This lesson follows a teardown failure into the report as a finding. The next lesson reads the gate that turns it into a failed run.

<LearnShell
  level="Level 4, lesson 5"
  minutes="About 6 minutes"
  outcome={[
    'Tell an observation, a finding and an attachment apart.',
    'Read a finding a failing teardown left.',
    'Say why a finding never replaces the test result.',
  ]}
  before={[
    <>Workbook as attachment (<Link to="/learn/evidence/workbook-as-attachment">lesson 4</Link>).</>,
    'The sample cloned for the failing teardown step; reading the report alone also works.',
  ]}
  situation={
    <>
      <p>A cleanup step throws after the test body passed. The runner summary says <code>Passed: 1</code>.</p>
      <p>A finding is evidence the run collected, not a test failure. This lesson walks the path from the attribute that throws to the report item.</p>
    </>
  }
  checkpoint={{
    question:
      'A coverage gap arrives as a Warning with category Coverage. Which of the three kinds is it, and what does it change about the result the test reported?',
    verify: (
      <>
        Add the failing teardown attribute below to one journey, run it alone, and open <code>TestResults/Northstar.ProtoTest/report.json</code> under the sample. Compare the test result with the finding row beside it.
      </>
    ),
    reveal: (
      <>
        It is a finding. A finding explains an outcome. It never replaces the result the test reported.
      </>
    ),
  }}
  learned={[
    'An observation feeds coverage, a finding explains an outcome, an attachment carries bytes.',
    'A teardown failure is recorded as an Error finding without replacing the result the test reported.',
    'A Warning finding is recorded and still leaves the run passing.',
  ]}
  next={[
    {
      label: 'Run gates',
      to: '/learn/evidence/run-gates',
      note: 'The gate that reads the findings and fails the run.',
    },
    {
      label: 'Reporting',
      to: '/docs/observability/reporting',
      note: 'The JSON and HTML sinks, and the sections every report carries.',
    },
  ]}>

## Three kinds of evidence

The report holds more than results. Tell the three kinds apart before anything reads them:

| Kind | Reading |
| --- | --- |
| Observation | counts or lists what the run saw |
| Finding | something worth reporting that is deliberately not the test result |
| Attachment | the exact file the test read |

An observation never passes or fails.

An attachment is the exact file the test read.

A finding sits between them. The [contract coverage lesson](/learn/evidence/contract-coverage) reads observations, and the attachment lesson opens attachments.

## Reference

Who writes each kind, and where it lands:

| Kind | Who writes it | Where it lands |
| --- | --- | --- |
| Observation | clients and collectors, through `RecordObservation` | coverage and traffic rows in the report, operations in the trace |
| Finding | a test or the lifecycle, through `AddFinding` | a report item with kind `finding`, plus a finding event in the trace |
| Attachment | tests and clients, through `context.AddAttachment(...)` and `.CaptureAttachments()` | `resources/&lt;test id&gt;/artifact-N/...` inside the archive, with the bytes |

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

The body passes and the run still exits 1. This is what that looks like before you write any code:

```text
TearDown failed for test fixture Northstar.ProtoTest.Setup
TearDown : ProtoTest.Core.ProtoRunGateException : Run gate 'no error findings' failed: The run recorded error findings.

Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1
```

The `Passed` line is the framework view of the test. The `TearDown` lines carry the exception the run gate threw.

To produce it, add a file to the sample that fails after one test:

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

Compare the runner summary with the finding and gate rows in `TestResults/Northstar.ProtoTest/report.json`.

</LearnShell>
