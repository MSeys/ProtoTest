---
sidebar_position: 3
title: GitLab CI
description: "Run ProtoTest on GitLab CI and keep the trace, reports and runner output as job artifacts."
---

# GitLab CI

The same shape as the [GitHub Actions](./#provider-configurations) setup: one results directory, tests, then artifacts that upload even when the tests failed.

```yaml title=".gitlab-ci.yml"
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

- `when: always` is the GitLab spelling of `if: always()`, because the trace is most useful when the test step failed.
- `PROTOTEST_RESULTS` points at `$CI_PROJECT_DIR/TestResults/ProtoTest`, the directory the [wiring](./#put-every-artifact-in-one-place) reads.
- `expire_in: 14 days` bounds storage. A suite with browser diagnostics grows faster, so shorten it or keep failures longer per [retention](./#what-to-keep).
- A container-backed suite needs a Docker-capable runner. Your runner setup decides if Docker-in-Docker is allowed and which service config it needs. ProtoTest only needs a reachable Docker endpoint.
