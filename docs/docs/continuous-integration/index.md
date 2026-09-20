---
sidebar_position: 1
title: Continuous integration
description: Run ProtoTest in CI and keep the trace, reports and runner output together as build artifacts on GitHub Actions, Azure Pipelines or GitLab CI.
---

# Continuous integration

A CI run should leave more than a red or green line. Keep the runner result, HTML report and `.prototrace` together: the runner says *which* test failed, the report shows coverage and findings across the run, and the trace explains *where* the scenario diverged.

## Put every artifact in one place

Relative output paths resolve below the test project's build output. That is convenient locally, but forces CI to search through `bin/**`. Give CI one absolute directory instead:

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

The CI configuration below sets `PROTOTEST_RESULTS` to its artifact directory. Locally, the same setup continues to write below `TestResults/ProtoTest`.

:::warning[Treat traces as test output]

A trace can contain sanitized requests, response bodies, state values and attachments. ProtoTest removes authorization headers and browser input values, but your own attributes and attachments may still carry application data. Use private build artifacts where the suite touches private data, and apply the same retention policy as other test results.

:::

## GitHub Actions

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

`if: always()` matters: the trace is most useful when the test step failed. GitHub-hosted Linux runners have Docker available for Testcontainers-based infrastructure.

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

## What to keep

| Artifact | Keep when | Why |
| --- | --- | --- |
| `.prototrace` | Always; shorter retention for sensitive suites | Complete execution story and attachments |
| HTML report | Always | Human-readable run, coverage, gates and findings |
| JSON report | When another job consumes it | Machine-readable run summary |
| Runner output (`.trx`, NUnit XML, JUnit XML) | Always | Native CI test history and annotations |
| Playwright trace and screenshots | On failure, or for a short retention period | Browser-specific diagnostics carried inside `.prototrace` and by the runner |

Open a downloaded `.prototrace` in the [ProtoTrace viewer](https://trace.prototest.dev). The file is read locally in the browser and is not uploaded to the viewer.

## Related

- [ProtoTrace](../observability/prototrace.md) explains the archive and viewer.
- [Reporting](../observability/reporting.md) configures JSON and HTML sinks.
- [Attachments](../foundation/attachments.md) explains what runners publish.
- [Troubleshooting](../getting-started/troubleshooting.md#the-ci-artifact-is-empty) covers missing CI output.
