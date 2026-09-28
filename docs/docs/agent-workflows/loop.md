---
sidebar_position: 5
title: Loop
description: "The evidence loop end to end: fail, evidence, fix, verify, report, with the GitHub Action that posts the digest and uploads the trace."
---

# Loop

The **evidence loop** is fail, evidence, fix, verify, report. One file carries the evidence: the `.prototrace` archive, with the run's report embedded. Every step reads the same archive, so the CI log, the agent and the pull request cannot tell different stories.

## The loop at a glance

| Step | What happens | What reads it |
| --- | --- | --- |
| Fail | the suite runs and one test does not pass; the trace and its report are written | `list_runs`, the check annotations |
| Evidence | the failure and its context are read | `get_failure`, `get_diagnosis` with `detail=context`, `prototest summary` |
| Fix | the code the context named is edited | the source snippet, the subject, the mismatches |
| Verify | the suite reruns and the new report is compared with the baseline | `prototest verify`, `get_coverage` |
| Report | the digest is posted and the trace is uploaded | `prototest feedback`, the action |

## Run it locally

Start with a failing test. Run the suite, then read the trace:

```bash
dotnet test
prototest summary TestResults/ProtoTest/run.prototrace
```

[Diagnosis](./diagnosis.md) shows what that output says. A coding agent reads the same story through the MCP tools ([Setup](./setup.md)), or you can read the whole run in the viewer.

After the fix, run the suite again and compare the two reports:

```bash
dotnet test
prototest verify baseline.json TestResults/ProtoTest/report.json
```

Post the digest without a pull request:

```bash
prototest feedback TestResults/ProtoTest/run.prototrace --digest digest.json
```

The annotations go to stdout, the per-channel outcomes go to stderr, and `--digest` writes the digest JSON beside the trace. With no target configured, the network channels skip with their reason, so a local run is safe. The committed MCP fixture prints:

```text
::error file=artifacts/fixture-gen/Program.cs,line=65::orders match their shape: Shape mismatch failed with 1 error(s):%0D%0A  • [$.orderId]: Values did not match. (Expected: '7', Actual: '42')
prototest feedback: github-annotations posted (1 annotation.)
prototest feedback: github-pr-comment skipped (No GitHub token: set GITHUB_TOKEN.)
prototest feedback: webhook skipped (No webhook URL: set PROTOTEST_FEEDBACK_WEBHOOK_URL.)
```

The comment and webhook targets are environment variables, the same names GitHub Actions provides: `GITHUB_TOKEN`, `GITHUB_REPOSITORY`, `GITHUB_EVENT_PATH` for the pull request number, `PROTOTEST_FEEDBACK_TRACE_URL` for the artifact link, and `PROTOTEST_FEEDBACK_WEBHOOK_URL` plus an optional secret header for the webhook.

## Wire it into the pull request

The *ProtoTest Feedback* action installs the CLI, uploads the trace as one artifact, posts the digest and runs the verdict. The suite writes its trace and report under `PROTOTEST_RESULTS`, the pattern the [CI page](../continuous-integration/index.md#put-every-artifact-in-one-place) sets up. A workflow:

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

- `if: always()` matters. The step runs when the test step failed, which is when the evidence is needed.
- `issues: write` lets the action comment on the pull request. The annotations and the artifact upload need nothing extra.
- The comment carries the artifact link, so the reviewer opens the trace from the comment.
- The action fails when a channel that reached its target failed, so a broken post is not silent.

### Add the verdict

Give the action the two reports and the pull request step becomes the gate:

```yaml
        with:
          trace: ${{ env.PROTOTEST_RESULTS }}/run.prototrace
          baseline-report: baseline/report.json
          current-report: ${{ env.PROTOTEST_RESULTS }}/report.json
```

The baseline is the report from the default branch. How it reaches the job is up to you: an artifact from the latest run on the default branch, a nightly job that publishes it, or a report checked into the repository. The action only needs the path. [Verification](./verification.md) explains the verdict and its finding classes.

## What the reviewer sees

- A pull request comment with the run, every test that did not pass, the cause, and the trace link.
- One check annotation per failing test, at its source location, and one per failed run gate.
- One uploaded artifact: the `.prototrace` archive with the report inside. Open it in the [ProtoTrace viewer](https://trace.prototest.dev).
- Locally, `prototest index TestResults` writes a static page over a folder of runs, with each run's outcome counts and links to its trace and digest ([ProtoTrace](../observability/prototrace.md)).

## Limits

- The digest is post-run by design. It is built from the written archive, after the sinks ran. An in-run sink cannot post it: the archive is written last, and a failed assertion is not a report item.
- The comment posts only when the digest carries a failure or a failed run gate. A green run's status check is the report. The webhook posts every digest, because a machine consumer decides what to do with it.
- A missing target skips its channel with a named reason. A target that is reached and refuses fails its channel, and the CLI exits `1`.
- A pull request from a fork gets a read-only token, so the comment channel cannot post and fails with its reason. The annotations and the artifact upload still work.
- The action is a composite action in the main repository. There is no marketplace listing yet; pin a release tag when one exists.
- Nothing leaves the machine unless you configure a target.

Open the pull request comment, follow the trace link, and open the archive in the viewer. The comment carries the same digest your agent read.
