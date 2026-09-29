---
sidebar_position: 1
title: Continuous integration
description: Run ProtoTest in CI and keep the trace, reports and runner output together as build artifacts on GitHub Actions, Azure Pipelines or GitLab CI.
---

# Continuous integration

A CI run should leave more than a red or green line. Keep the runner result, the HTML report and the `.prototrace` together: the runner says which test failed, the report shows coverage and findings across the run, and the trace shows where the scenario diverged.

## Put every artifact in one place

Relative output paths resolve below the test project's build output. That is convenient locally, but it forces CI to search through `bin/**`. Give CI one absolute directory instead:

```csharp
var results = Environment.GetEnvironmentVariable("PROTOTEST_RESULTS")
    ?? Path.Combine("TestResults", "ProtoTest");

builder
    .ConfigureTracing(trace =>
        trace.OutputPath = Path.Combine(results, "run.prototrace"))
    .AddSink<JsonReportSink>(sink =>
        sink.OutputPath = Path.Combine(results, "report.json"))
    .AddSink<HtmlReportSink>(sink =>
        sink.OutputPath = Path.Combine(results, "report.html"));
```

The CI configurations below set `PROTOTEST_RESULTS` to their artifact directory. Locally, the same setup keeps writing below `TestResults/ProtoTest`.

:::warning[Treat traces as test output]
A trace can contain sanitized requests, response bodies, state values and attachments. ProtoTest removes authorization headers and browser input values, but your own attributes and attachments may still carry application data. Use private build artifacts where the suite touches private data, and apply the same retention policy as other test results.
:::

## The feedback action

The *ProtoTest Feedback* composite action runs the post-run half in one step: it installs the [ProtoTest CLI](../agent-workflows/cli.md), uploads the trace as an artifact, posts the digest where a pull request reads it, and optionally gates the pull request against a baseline report.

The digest is the same run summary the CLI prints: the run, every test that did not pass with its cause and source location, and the artifact link. The action turns it into a pull request comment, one check annotation per failing test and per failed run gate, and one uploaded artifact. A green run posts no comment; the status check is its report.

```yaml
name: Integration tests

on:
  pull_request:

permissions:
  contents: read
  issues: write

jobs:
  test:
    runs-on: ubuntu-latest
    env:
      PROTOTEST_RESULTS: ${{ github.workspace }}/TestResults/ProtoTest

    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x

      - run: dotnet restore
      - run: dotnet test --configuration Release --no-restore

      - name: Post the evidence
        if: always()
        uses: MSeys/ProtoTest/.github/actions/feedback@main
        with:
          trace: ${{ env.PROTOTEST_RESULTS }}/run.prototrace
```

- `if: always()` matters: the step runs when the test step failed, which is when the evidence is needed.
- `issues: write` lets the action comment on the pull request. The annotations and the artifact upload need nothing extra.
- The comment carries the artifact link, so a reviewer opens the trace from the comment.
- The action fails when a channel that reached its target failed, so a broken post is not silent.
- `@main` tracks the default branch. Pin a commit SHA for a pipeline that does not move under you, and switch to a release tag once one exists.

### Add the verdict

Give the action the two reports and the pull request step becomes the gate. Something has to produce the baseline: a nightly job runs the same suite on the default branch and keeps its `report.json`, and the pull request job fetches it.

```yaml
# nightly.yml
name: Nightly baseline

on:
  schedule:
    - cron: '30 2 * * *'

jobs:
  baseline:
    runs-on: ubuntu-latest
    env:
      PROTOTEST_RESULTS: ${{ github.workspace }}/TestResults/ProtoTest

    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x

      - run: dotnet restore
      - run: dotnet test --configuration Release --no-restore

      - name: Keep the baseline report
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: prototest-baseline
          path: ${{ env.PROTOTEST_RESULTS }}/report.json
          if-no-files-found: error
```

The pull request job downloads the newest nightly artifact and hands the action both reports. It needs `actions: read` in the workflow's permissions for the download:

```yaml
      - name: Fetch the nightly baseline
        env:
          GH_TOKEN: ${{ github.token }}
        run: |
          run_id=$(gh run list --workflow nightly.yml --branch main --limit 1 --json databaseId --jq '.[0].databaseId')
          gh run download "$run_id" --name prototest-baseline --dir baseline

      - name: Post the evidence
        if: always()
        uses: MSeys/ProtoTest/.github/actions/feedback@main
        with:
          trace: ${{ env.PROTOTEST_RESULTS }}/run.prototrace
          baseline-report: baseline/report.json
          current-report: ${{ env.PROTOTEST_RESULTS }}/report.json
```

The step fails the pull request when the run is worse than the baseline; without the two reports it only posts the digest. To run the same comparison locally, end the loop with `prototest verify baseline/report.json TestResults/ProtoTest/report.json`. [Verification](../agent-workflows/verification.md) explains the verdict and its finding classes.

### Pin the tool

The action installs the ProtoTest CLI as a global tool. Set `version` to pin it (`version: 1.1.0`). Without it, the action updates the tool to the latest stable release. `dotnet-roll-forward` defaults to `LatestMajor`, so the net8 tool runs on a newer runtime.

The action's other inputs are optional. `webhook-url` and `webhook-secret` post the digest JSON to your own endpoint, with `webhook-secret-header` naming the shared-secret header (default `X-ProtoTest-Secret`); `artifact-name` names the uploaded trace (default `prototest-trace`). [Loop](../agent-workflows/loop.md) explains what each channel posts; the [CLI reference](../agent-workflows/cli.md) lists every environment target and the exit codes.

## GitHub Actions

If you prefer to keep only the raw artifacts, this job runs the suite and uploads the results folder:

```yaml
name: Integration tests

on:
  push:
  pull_request:

permissions:
  contents: read

jobs:
  test:
    runs-on: ubuntu-latest
    env:
      PROTOTEST_RESULTS: ${{ github.workspace }}/TestResults/ProtoTest

    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x

      - run: dotnet restore
      - run: dotnet test --configuration Release --no-restore

      - name: Keep ProtoTest evidence
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: prototest-results
          path: TestResults/ProtoTest
          if-no-files-found: error
```

`if: always()` matters here too: the trace is most useful when the test step failed. GitHub-hosted Linux runners have Docker available for Testcontainers-based infrastructure.

### Playwright on Linux

When the suite uses the bundled Playwright browsers, build once, install the browser and its Linux dependencies, then test without rebuilding:

```yaml
- run: dotnet build --configuration Release --no-restore

- name: Install Chromium
  shell: pwsh
  run: |
    $installer = Get-ChildItem -Recurse -Filter playwright.ps1 |
      Where-Object FullName -Match 'bin/Release' |
      Select-Object -First 1
    if (-not $installer) { throw "playwright.ps1 was not generated." }
    pwsh $installer.FullName install --with-deps chromium

- run: dotnet test --configuration Release --no-build --no-restore
```

If `InstallBrowsers = true` is enabled in ProtoTest, it downloads a missing browser binary itself. The explicit CI step remains useful on Linux because Playwright also installs the operating-system libraries the browser needs.

## Azure Pipelines

```yaml
trigger:
  - main

pool:
  vmImage: ubuntu-latest

variables:
  PROTOTEST_RESULTS: $(Build.ArtifactStagingDirectory)/ProtoTest

steps:
  - task: UseDotNet@2
    inputs:
      packageType: sdk
      version: 10.x

  - task: DotNetCoreCLI@2
    inputs:
      command: test
      arguments: --configuration Release

  - task: PublishPipelineArtifact@1
    condition: succeededOrFailed()
    inputs:
      targetPath: $(PROTOTEST_RESULTS)
      artifact: prototest-results
```

Use `succeededOrFailed()` for the same reason as GitHub's `always()`: publish the evidence even when a test fails.

## GitLab CI

```yaml
integration-tests:
  image: mcr.microsoft.com/dotnet/sdk:10.0
  variables:
    PROTOTEST_RESULTS: "$CI_PROJECT_DIR/TestResults/ProtoTest"
  script:
    - dotnet restore
    - dotnet test --configuration Release --no-restore
  artifacts:
    when: always
    paths:
      - TestResults/ProtoTest/
    expire_in: 14 days
```

A container-backed suite also needs a Docker-capable GitLab runner. Whether Docker-in-Docker is allowed and which service configuration is required belongs to the runner installation; ProtoTest only needs the Docker endpoint to be reachable.

## One suite, three jobs

A pipeline around this suite usually splits into three jobs:

- The **pull request** job runs the suite in-process, posts the digest and compares reports against the nightly baseline. It is the fast one and runs on every change.
- The **nightly** job runs the same suite against the container topology, where the store and the broker are real processes the run owns, and publishes the baseline report.
- The **smoke** job is optional and points the suite at a deployed environment. Capability skips drop the journeys that need the test host, and the rest run against real addresses.

The suite is the same in all three. What changes is the composition, and the composition decides which capabilities exist and which journeys skip. These jobs are the shapes, not a fixed pipeline; the workflows above are the ones to start from.

## What to keep

| Artifact | Keep when | Why |
| --- | --- | --- |
| `.prototrace` | Always; shorter retention for sensitive suites | Complete execution story and attachments |
| HTML report | Always | Human-readable run, coverage, gates and findings |
| JSON report | When another job consumes it | Machine-readable run summary |
| Runner output (`.trx`, NUnit XML, JUnit XML) | Always | Native CI test history and annotations |
| Playwright trace and screenshots | On failure, or for a short retention period | Browser-specific diagnostics carried inside `.prototrace` and by the runner |

The action [above](#the-feedback-action) uploads the trace for you. Without it, upload the results folder as an artifact. Open a downloaded `.prototrace` in the [ProtoTrace viewer](https://trace.prototest.dev); the file is read locally in the browser and is not uploaded to the viewer.

## Related

- [ProtoTrace](../observability/prototrace.md) explains the archive and the viewer.
- [Reporting](../observability/reporting.md) configures the JSON and HTML sinks.
- [Attachments](../foundation/attachments.md) explains what runners publish.
- [Loop](../agent-workflows/loop.md) turns the same artifacts into a pull request verdict and comment.
- [CLI reference](../agent-workflows/cli.md) documents the four verbs, the environment targets and the exit codes.
- [Troubleshooting](../getting-started/troubleshooting.md#the-ci-artifact-is-empty) covers missing CI output.
