---
id: evidence-in-ci
title: Keep the evidence when CI fails
sidebar_label: Evidence in CI
sidebar_position: 9
description: "Put the trace and the reports in one directory, upload it even when the test step fails, and post the digest with the action."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import Link from '@docusaurus/Link';

# Keep the evidence when CI fails

<Lesson
  track="Understand failures"
  step="Lesson 9 of 9"
  minutes={8}
  outcomes={[
    'Point every output at one directory and upload it as one CI artifact',
    'Keep the evidence when the test step fails',
    'Post the digest with the action',
  ]}
  needs={[
    <>The previous lesson, <Link to="/learn/understand-failures/artifacts-and-reports">archive and reports</Link></>,
    'A repository with CI, if you want to try the workflow. Reading it also works.',
  ]}
/>

## The problem

The suite runs in CI and a job turns red with one line in the log. When the job ends, the files are gone, and nobody can open the trace.

The suite already wrote the trace and the reports. The job has to keep them, and one step turns them into a comment the reviewer can open.

## Do it

### 1. Write everything to one directory

Relative output paths resolve below the test project's build output, which forces CI to search `bin/**`. Give CI one absolute directory instead:

<AnnotatedCode
  filename="Setup.cs"
  code={`var results = Environment.GetEnvironmentVariable("PROTOTEST_RESULTS")
    ?? Path.Combine("TestResults", "ProtoTest");

builder
    .ConfigureTracing(trace =>
        trace.OutputPath = Path.Combine(results, "run.prototrace"))
    .AddSink<JsonReportSink>(sink =>
        sink.OutputPath = Path.Combine(results, "report.json"))
    .AddSink<HtmlReportSink>(sink =>
        sink.OutputPath = Path.Combine(results, "report.html"));`}
  callouts={[
    {line: 1, title: 'Let CI name the directory', note: 'The environment variable is the job\'s artifact directory; a local run keeps the default below TestResults.'},
    {line: 5, title: 'Trace and reports in one place', note: 'One upload step then carries the story and the report, which is what makes the artifact link useful.'},
  ]}
  foot={<>The sink registration the sample uses, with CI's artifact directory in front. Locally the fallback keeps the same files below <code>TestResults/ProtoTest/</code>.</>}
/>

In the workflow, set the variable once for the job: `PROTOTEST_RESULTS: ${{ github.workspace }}/TestResults/ProtoTest`. After a run, `run.prototrace`, `report.json` and `report.html` sit together in that directory.

### 2. Upload it even when the test step fails

```yaml
- name: Keep the evidence
  if: always()
  uses: actions/upload-artifact@v4
  with:
    name: prototest-results
    path: TestResults/ProtoTest
    if-no-files-found: error
```

The `if: always()` line is the one that matters. Without it the upload is skipped exactly when the trace is worth reading, because a failing test fails the step that ran it.

### 3. Post the digest

The evidence action uploads the trace, compares the run with the base branch's last green run, and posts what changed:

```yaml
- name: ProtoTest evidence
  if: always()
  uses: MSeys/prototest-action@v1
  with:
    trace: ${{ env.PROTOTEST_RESULTS }}/run.prototrace
```

The comment names each test the change broke or fixed and the operation where it changed, then the failing tests with their cause and the artifact link. Each failing test also gets a check annotation at its source location. The step fails when a test that passed on the base branch fails now. The workflow needs `actions: read` to fetch the base branch's trace and `pull-requests: write` to comment. The [CI page](/docs/continuous-integration/#the-evidence-action) shows the whole workflow.

### 4. Name the build in the trace

A trace downloaded from CI should say which build made it. Name the variables once and every report carries them:

```csharp
builder.ConfigureTracing(trace =>
{
    trace.RunMetadataEnvironmentVariables.Add("GITHUB_RUN_ID");
    trace.RunMetadataEnvironmentVariables.Add("GITHUB_SHA");
});
```

Each entry appears in the report's run metadata and on the run in the archive, so a trace three months old still names the commit it tested.

## What happened

CI gave you one directory, one upload step and one comment. The `if: always()` on both steps means a failed run keeps its evidence and gets its comment. The reviewer opens the trace in the viewer without rerunning the job.

The same wiring works in every job you split the pipeline into: pull request, nightly or a smoke run against a deployed environment. Only the composition changes.

## Check yourself

<Checkpoint
  question="The test step failed. Which steps still run, and what does the reviewer open from the comment?"
  verify={<>Read <Link to="/docs/continuous-integration/#put-every-artifact-in-one-place">Put every artifact in one place</Link> and <Link to="/docs/continuous-integration/#the-evidence-action">The evidence action</Link> on the CI page, then the two steps above that carry <code>if: always()</code>.</>}>

The upload and the post run with `if: always()`, so the failed run keeps its evidence and gets its comment. The comment names the tests that did not pass and links the trace artifact. The reviewer opens it in the viewer without rerunning the job.

</Checkpoint>

## Remember

- One output directory and one artifact step keep the trace and the reports together.
- The artifact step runs with `if: always()`, because the failed run is the one worth reading.
- The action posts the digest and the artifact link. The job split around it is up to you.

## Go deeper

- [Continuous integration](/docs/continuous-integration/): the working workflow and the action. Its [three jobs](/docs/continuous-integration/#one-suite-three-jobs) are the ones to start from.
- [Loop](/docs/agent-workflows/loop): the same archive read by an agent: fail, evidence, fix, verify, report.
