---
id: evidence-in-ci
title: Take the evidence to CI
sidebar_label: Take the evidence to CI
sidebar_position: 5
description: "Keep the trace and the reports as CI artifacts, post the digest with the action, and run the same suite at three depths."
---

import LearnShell from '@site/src/components/LearnShell';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import Link from '@docusaurus/Link';

# Take the evidence to CI

A red job that prints one line is not evidence. The suite already wrote the trace and the reports. The job must keep them. One step turns them into a comment the reviewer can open.

<LearnShell
  level="Level 4, lesson 5"
  minutes="About 9 minutes"
  outcome={[
    'Point every output at one directory and upload it as one CI artifact.',
    'Keep the evidence when the test step fails, not only when it passes.',
    'Post the digest with the action, and name the three jobs a pipeline uses.',
  ]}
  before={[
    <>The archive and the reports (<Link to="/learn/evidence/artifacts-and-reports">lesson 3</Link>).</>,
    'A repository with CI, if you want to try the workflow. Reading it also works.',
  ]}
  situation={
    <>
      <p>The same suite runs in CI and on your machine. In CI nobody can open the trace from the test output folder, and the job is the last moment the files exist: once it ends, the runner is gone.</p>
      <p>The setup below gives CI one directory to upload, keeps it on failure, and posts a digest with the archive link. The reference pages carry the working workflow; this lesson is the shape of it.</p>
    </>
  }
  checkpoint={{
    question:
      'The test step failed. Which steps still run, and what does the reviewer open from the comment?',
    verify: (
      <>
        Read <Link to="/docs/continuous-integration/#put-every-artifact-in-one-place">Put every artifact in one place</Link> and <Link to="/docs/continuous-integration/#the-feedback-action">The feedback action</Link> on the CI page, then the two steps in this lesson that carry <code>if: always()</code>. The three jobs further down are shapes a pipeline can take, not the contract.
      </>
    ),
    reveal: (
      <>
        The upload and the post run with <code>if: always()</code>, so the failed run keeps its evidence and gets its comment. The comment names the tests that did not pass and links the trace artifact; the reviewer opens it in the viewer without rerunning the job.
      </>
    ),
  }}
  learned={[
    'One output directory and one artifact step keep the trace and the reports together.',
    'The artifact step runs with if: always(), because the failed run is the one worth reading.',
    'The action posts the digest and the artifact link; the verdict comes from comparing two reports.',
    'The job split is a shape a pipeline can take. The action and the artifact wiring are the contract.',
  ]}
  next={[
    {
      label: 'Loop',
      to: '/docs/agent-workflows/loop',
      note: 'The same archive read by an agent: fail, evidence, fix, verify, report.',
    },
    {
      label: 'Continuous integration',
      to: '/docs/continuous-integration/',
      note: 'The working workflow and the action.',
    },
  ]}>

## One directory for the evidence

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

## Keep it when the test step fails

```yaml
- name: Keep the evidence
  if: always()
  uses: actions/upload-artifact@v4
  with:
    name: prototest-results
    path: TestResults/ProtoTest
    if-no-files-found: error
```

The `if: always()` is the line that matters. Without it the upload is skipped exactly when the trace is worth reading, because a failing test fails the step that ran it.

## Post the digest

The feedback action installs the CLI, uploads the trace, posts the digest and checks it against a baseline when both reports are given:

```yaml
- name: Post the evidence
  if: always()
  uses: MSeys/ProtoTest/.github/actions/feedback@main
  with:
    trace: ${{ env.PROTOTEST_RESULTS }}/run.prototrace
```

The comment carries the failing tests, the cause and the artifact link. One check annotation lands on each failing test's source location, and a missing target skips with its reason instead of failing the job. Give the action a `baseline-report` and a `current-report` as well, and the step also fails the pull request when the run is worse than the baseline.

## Three shapes, one suite

A pipeline around this suite usually splits into three jobs:

- The **pull request** job runs the suite in-process and posts the digest. It is the fast one, and it runs on every change.
- The **nightly** job runs the same suite against the container topology, where the store and the broker are real processes the run owns.
- The **smoke** job is optional and points the suite at a deployed environment. Capability skips drop the journeys that need the test host, and the rest run against real addresses.

These are shapes a suite of this kind fits, not a fixed pipeline. The [CI page](/docs/continuous-integration/#one-suite-three-jobs) carries the same three, and its workflows are the ones to start from. The suite is the same in all three. What changes is the composition, and the composition is what decides which capabilities exist and which journeys skip.

## Naming the build in the trace

A trace downloaded from CI should say which build it came from. Name the variables once and every report carries them:

```csharp
builder.ConfigureTracing(trace =>
{
    trace.RunMetadataEnvironmentVariables.Add("GITHUB_RUN_ID");
    trace.RunMetadataEnvironmentVariables.Add("GITHUB_SHA");
});
```

Each entry appears in the report's run metadata section and on the run in the archive, so a trace three months old still names the commit it tested.

</LearnShell>