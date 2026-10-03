---
sidebar_position: 4
title: Verification
description: "Compare two runs' reports and return the pull request verdict: coverage regressions, new uncovered units, specification identity and failed run gates."
---

# Verification

A green run shows your tests still pass. It does not show your covered contract is still covered. Verification compares two runs' reports and returns a verdict a pull request gate can use.

```bash
prototest verify baseline.json current.json
```

The **baseline** is the report from the default branch. The **current** report is the pull request's run. Both are the JSON reports a `ProtoTest.Reporting` sink wrote, as files or embedded in the runs' traces, so the base branch's kept trace is the baseline. [Reporting](../observability/reporting.md) covers the sink and its options, and the [CI page](../continuous-integration/index.md#compare-with-the-base-branch) shows the workflow that produces both.

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

Every line of the block says one thing. The `::error` line is the check annotation GitHub renders, and the rest is the verdict a reader scans.

| Line | What it is |
| --- | --- |
| `::error::regressed: ...` | the failing finding as a workflow command, one per fail |
| `ProtoTest verification failed: 1 failing, 0 warning(s), 0 info` | the verdict, and the counts by severity |
| `fail regressed: ...` | the findings again, as text |
| `coverage deltas:` and the rows under it | every target and category pair, including the ones that did not move |

The command exits `0` when no finding is a `fail`, and `1` when one is, so CI can use it as the gate. The [CLI reference](./cli.md#exit-codes) lists the exit codes.

The verdict also prints a `spec checks:` block when specifications were compared. Only a library caller can compare candidate specifications. The verb reports the identity each run recorded.

## What it finds

| Class | Default severity | Meaning |
| --- | --- | --- |
| `regressed` | fail | a unit the baseline covered is uncovered now |
| `added-uncovered` | warn | a unit now reported uncovered that the baseline did not have |
| `stale-spec` | fail | the recorded specification identity does not match the content, or the specification changed between the two runs |
| `gate-failed` | fail | a run gate that failed in the current run |

Those four defaults are a pull request gate. The library lets a consumer override each severity, and the CLI uses the defaults. A finding also carries the target, the category, the unit or gate identifier, the baseline and current values, and an evidence pointer.

## The specification identity

The OpenAPI and GraphQL coverage collectors record one aggregate coverage item per specification target. It holds `spec.source` and `spec.hash`. `spec.source` omits URL credentials and known token query values, and `spec.hash` is the SHA-256 of the loaded content. The item has no covered verdict, so coverage totals, run gates and the report percentage ignore it ([OpenAPI coverage](../integrations/openapi.md#the-specification-source)).

Verification compares that recorded hash with candidate content. A library caller hands the files in:

```csharp
var baseline = ProtoVerificationRun.FromReportFile("baseline.json");
var current = ProtoVerificationRun.FromReportFile("current.json");
var verdict = ProtoVerification.Verify(baseline, current,
[
    new ProtoSpecCandidate("Northstar:Api", "openapi.json")
]);
```

A remote specification source is recorded, not fetched. Without a candidate, the identity is recorded, not re-verified. The `prototest verify` verb takes no spec candidates, so that comparison is a library call.

## Gate the pull request

The [ProtoTest Evidence action](../continuous-integration/index.md#compare-with-the-base-branch) puts the coverage that moved in the pull request comment, with the test to start from for each new uncovered unit, and runs this verdict over the reports the base branch's trace and the pull request's trace embedded, and fails the step on a failing finding. `prototest verify` reads a `.prototrace` as well as a `report.json`, so the same check runs locally over two traces. The feedback channel has already annotated each failing test at its source location, so the reviewer sees the failure where it happened.

## Limits

- The verdict reads reports, the files or the ones traces embedded. No coverage is recomputed from spans. The arithmetic is the report's own (`CoverageUnits` and `CoverageTotals`).
- Units are matched by target, category and path, so an OpenAPI property, a GraphQL field and a REST route compare by their own vocabulary. A nested unit's path puts its identifier under its parents' (`GET /orders › 200 › $.id`), so the same response or property under two endpoints stays two units; an identifier its collector already qualified (`Query.orders`) stays as it is.
- A unit or a target that disappeared from the candidate is not a finding. The delta row shows it instead.
- Unmatched traffic and schema drift are out of scope. Either a collector records them, or Verification does not claim them.
- Failed candidate run gates are surfaced from the report's gate items. Verification does not add run gates and does not duplicate a suite's single-run coverage threshold, which stays in a run gate.
- Nothing is written and nothing is rerun.

Write two reports, run the command, and read the verdict line. The same verb runs in the action, where the verdict fails the pull request step.
