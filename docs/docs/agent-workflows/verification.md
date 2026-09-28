---
sidebar_position: 4
title: Verification
description: "Compare two runs' reports and return the pull request verdict: coverage regressions, new uncovered units, specification identity and failed run gates."
---

# Verification

A green run proves that the tests you have still pass. It does not prove that the contract you already covered is still covered. Verification compares two runs' reports and returns a verdict a pull request gate can use.

## Write the two reports

The input is the JSON report a `ProtoTest.Reporting` sink wrote:

```csharp
builder.AddSink<JsonReportSink>(sink => sink.OutputPath = "TestResults/report.json");
```

The **baseline** is the report from the default branch. The **current** report is the pull request's run. Both are plain files, so CI can keep the baseline as an artifact and download it into the pull request job. [Reporting](../observability/reporting.md) covers the sink and its options.

## Run the verdict

```bash
prototest verify baseline.json current.json
```

A regression prints this. The example comes from a real run of the command over two report files, where the current report has one unit uncovered that the baseline covered.

```text
::error::regressed: Target 'Northstar:Api' unit 'GET /api/v1/orders' in category 'OpenAPI' was covered in the baseline and is uncovered now.
ProtoTest verification failed: 1 failing, 0 warning(s), 0 info
  fail regressed: Target 'Northstar:Api' unit 'GET /api/v1/orders' in category 'OpenAPI' was covered in the baseline and is uncovered now.
coverage deltas:
  Northstar:Api · OpenAPI: 1/2 covered -> 0/2 covered (-50 points, 1 regressed, 0 added uncovered)
  Northstar:Api · OpenAPI Property: 0/1 covered -> 0/1 covered (0 points, 0 regressed, 0 added uncovered)
  Northstar:Api · OpenAPI Response: 1/1 covered -> 1/1 covered (0 points, 0 regressed, 0 added uncovered)
```

- The command exits `0` when no finding is a `fail`, and `1` when one is, so CI can use it as the gate.
- Every failing finding also prints one `::error` workflow command, which a GitHub runner turns into an annotation on the run.
- The verdict line counts the findings by severity.
- The coverage deltas list every target and category pair, including the ones that did not move, so the numbers behind the verdict are visible.
- When candidate specifications were compared, the verdict also lists each target's spec check.

## What it finds

| Class | Default severity | Meaning |
| --- | --- | --- |
| `regressed` | fail | a unit the baseline covered is uncovered in the candidate |
| `added-uncovered` | warn | a unit the candidate reports uncovered that the baseline did not have |
| `stale-spec` | fail | the recorded specification identity does not match the bytes the candidate carries, or the specification changed between the two runs |
| `gate-failed` | fail | a run gate that failed in the candidate run |

Those four defaults are a pull request gate. The library lets a consumer override each severity; the CLI uses the defaults. A finding also carries the target, the category, the unit or gate identifier, the baseline and current values, and an evidence pointer.

## The specification identity

The OpenAPI and GraphQL coverage collectors record one aggregate coverage item per specification target: `spec.source` (the configured source, with URL credentials and known token query values removed) and `spec.hash` (the SHA-256 of the loaded content). The item has no covered verdict, so coverage totals, run gates and the report percentage ignore it ([OpenAPI coverage](../integrations/openapi.md#the-specification-source)).

Verification compares that recorded hash with a candidate file you hand it in code:

```csharp
var baseline = ProtoVerificationRun.FromReportFile("baseline.json");
var current = ProtoVerificationRun.FromReportFile("current.json");
var verdict = ProtoVerification.Verify(baseline, current,
[
    new ProtoSpecCandidate("Northstar:Api", "openapi.json")
]);
```

A remote specification source is recorded but never fetched. Without a candidate, the identity is recorded, not re-verified. The `prototest verify` verb does not take spec candidates in 1.1; that comparison is a library capability today.

## Gate the pull request

The [ProtoTest Feedback action](./loop.md#wire-it-into-the-pull-request) takes the same two reports:

```yaml
with:
  trace: ${{ env.PROTOTEST_RESULTS }}/run.prototrace
  baseline-report: baseline/report.json
  current-report: ${{ env.PROTOTEST_RESULTS }}/report.json
```

A failing verdict fails the action step, which fails the job. The feedback channel has already annotated each failing test at its source location, so the reviewer sees the failure where it happened.

## Limits

- The comparison reads reports, not traces. No spans are re-read and no coverage is recomputed; the arithmetic is the report's own (`CoverageUnits` and `CoverageTotals`).
- Units are matched by target, category and identifier, so an OpenAPI property, a GraphQL field and a REST route compare by their own vocabulary.
- A unit or a target that disappeared from the candidate is not a finding. The delta row shows it instead.
- Unmatched traffic and schema drift are out of scope: either a collector records them or Verification does not claim them.
- Failed candidate run gates are surfaced from the report's gate items. Verification does not add run gates and does not duplicate a suite's single-run coverage threshold, which stays in a run gate.
- Nothing is written and nothing is rerun.

Write two reports, run the command, and read the verdict line. The same verb runs in the action, where the verdict fails the pull request step.
