---
sidebar_position: 5
title: The evidence loop
sidebar_label: The evidence loop
description: "The evidence loop end to end: fail, evidence, fix, verify, report, with the pull request comment and the check annotations each step produces."
---

# The evidence loop

The **evidence loop** is fail, evidence, fix, verify, report. One file carries the evidence: the `.prototrace` archive with the embedded report. Every step reads the same archive.

## What the reviewer sees

A failing run leaves three things on the pull request. The comment below is illustrative; its values come from the committed failing fixture:

```markdown
## ProtoTest run `29e344f9cf54431ca7d8bad3f87a1749`

**2 tests · 1 failed · 1 succeeded**

- **FAILED `orders match their shape`** (16 ms)
  - `assert.json.shape` · failed
  - Shape mismatch failed with 1 error(s):
    • [$.orderId]: Values did not match. (Expected: '7', Actual: '42')
  - at `artifacts/fixture-gen/Program.cs:65`
  - mismatch `$.orderId`: expected 7, actual 42

Coverage: 2/4 (50%)

[Full trace](https://github.com/you/your-repo/actions/runs/1/artifacts/prototest-trace)
```

Next to it, one check annotation per failing test at its source location, one per failed run gate, and one artifact: the `.prototrace` archive with the report inside. A green run posts no comment; its status check is the report.

| Step | What happens | Who reads it |
| --- | --- | --- |
| Fail | the suite runs and one test does not pass; the trace and its report are written | `list_runs`, the check annotations |
| Evidence | the failure and its context are read | `get_failure`, `get_diagnosis` with `detail=context`, `prototest summary` |
| Fix | the code the context named is edited | the source snippet, the subject, the mismatches |
| Verify | the suite reruns and the new report is compared with the baseline | `prototest verify`, `get_coverage` |
| Report | the digest is posted and the trace is uploaded | `prototest feedback`, the action |

## Run it locally

The same four commands in order, with the output each one prints. Start with a failing test.

**1. Run the suite and summarize the trace:**

```bash
dotnet test
prototest summary TestResults/ProtoTest/run.prototrace
```

```text
ProtoTest trace 2.0 · run 761778e6dc82498a9f9965fa1e6b5a24 · 2026-09-29 06:19:01Z - 2026-09-29 06:19:05Z
1 tests · 1 failed

FAILED Northstar.ProtoTest.FailureDrills.TheAddressWasHardcodedForOneMachine (2.66 s)
  ConnectionError reaching http://127.0.0.1:5099: connection refused.
  test.execution Test execution · failed
  cause: runner-reported failure
```

Run ids and timestamps are new on every run; compare the shape, not the values.

[Diagnosis](./diagnosis.md) reads that output line by line. A coding agent reads the same story through the MCP tools ([Setup](./setup.md)), or you can read the whole run in the viewer.

After the fix, run the suite again and compare the two reports.

**2. Verify the fix against the baseline:**

```bash
dotnet test
prototest verify baseline.json TestResults/ProtoTest/report.json
```

**3. Post the digest without a pull request:**

```bash
prototest feedback TestResults/ProtoTest/run.prototrace --digest digest.json
```

```text
::error::Northstar.ProtoTest.FailureDrills.TheAddressWasHardcodedForOneMachine: ConnectionError reaching http://127.0.0.1:5099: connection refused.
prototest feedback: github-annotations posted (1 annotation.)
prototest feedback: github-pr-comment skipped (No GitHub token: set GITHUB_TOKEN.)
prototest feedback: webhook skipped (No webhook URL: set PROTOTEST_FEEDBACK_WEBHOOK_URL.)
```

The first line is stdout: the annotations GitHub renders on the pull request. It is the bare form because this failure carries no source location; with one the command reads `::error file=path/to/OrderTests.cs,line=42::message`. The rest is stderr: one outcome per channel. `--digest` writes the digest JSON to the path you gave it. With no target configured the network channels skip with their reason, so a local run is safe.

The [CLI reference](./cli.md#environment-targets) lists every target the comment and the webhook read, and the exit codes.

## Wire it into the pull request

The *ProtoTest Feedback* action runs the post-run step. It installs the CLI, uploads the trace as one artifact, posts the digest and runs the verdict when both reports are given.

The workflow, the artifact folder and the `with:` block are on the [CI page](../continuous-integration/index.md#the-feedback-action). The suite writes its trace and report under `PROTOTEST_RESULTS`, the same wiring that page sets up, so one folder holds everything and one artifact step keeps it.

## Limits

- The digest is built after the run from the written archive. An in-run sink cannot post it. The archive is written last, and a failed assertion is not a report item.
- The comment posts only when the digest carries a failure or a failed run gate. The webhook posts every digest, because a machine consumer decides what to do with it (`Feedback_ShouldPostTheWebhookForAGreenRun` in `tests/ProtoTest.Feedback.Tests` pins the green-run half).
- A missing target skips its channel with a named reason. A target that is reached and refuses fails its channel, and the CLI exits `1`.
- A pull request from a fork gets a read-only token, so the comment channel cannot post and fails with its reason. The annotations and the artifact upload still work.
- Nothing leaves the machine unless you configure a target.

Open the pull request comment, follow the trace link, and open the archive in the viewer. The comment carries the same digest your agent read.
