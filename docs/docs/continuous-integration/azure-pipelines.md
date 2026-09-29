---
sidebar_position: 2
title: Azure Pipelines
description: "Run ProtoTest on Azure Pipelines and publish the trace, reports and runner output as a pipeline artifact."
---

# Azure Pipelines

The same shape as the [GitHub Actions](./#provider-configurations) setup: one results directory, tests, then a publish step that runs even when the tests failed.

```yaml title="azure-pipelines.yml"
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

- `condition: succeededOrFailed()` is the Azure spelling of `if: always()`: the trace is most useful when the test step failed.
- `PROTOTEST_RESULTS` points at `$(Build.ArtifactStagingDirectory)/ProtoTest`, the directory the [wiring](./#put-every-artifact-in-one-place) reads.
- Microsoft-hosted Linux agents have Docker, so a container-backed suite needs no extra runner setup.
- [Retention](./#what-to-keep) follows the same rules as everywhere: keep the trace and the HTML report, and expire them with the pipeline's own retention.
