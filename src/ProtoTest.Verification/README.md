# ProtoTest.Verification

> Preview: the surface can change before 1.2.

Compares two runs' reports and returns a verdict a pull request gate can use: coverage regressions,
new uncovered units, the specification identity the candidate was verified against, and the failed run
gates the candidate recorded. It also compares two traces test by test and names where each changed
test's runs part, and proves a fix from recorded runs.

```bash
dotnet add package ProtoTest.Verification
```

```csharp
var baseline = ProtoVerificationRun.FromReportFile("baseline.json");
var current = ProtoVerificationRun.FromReportFile("current.json");
var verdict = ProtoVerification.Verify(baseline, current,
[
    new ProtoSpecCandidate("Api", "openapi.json")
]);

if (verdict.Failed)
{
    foreach (var finding in verdict.Findings.Where(f => f.Severity == ProtoVerificationSeverities.Fail))
    {
        Console.WriteLine($"{finding.Class}: {finding.Message}");
    }
}
```

```csharp
var comparison = ProtoVerification.Compare("main.prototrace", "current.prototrace");
foreach (var test in comparison.Tests.Where(t => t.Change == ProtoTestChanges.Broken))
{
    Console.WriteLine($"{test.Name} broke at {test.Divergence?.Current?.Name}");
}
```

```csharp
var receipt = ProtoVerification.Prove("before.prototrace", ["after.prototrace"], ["orders are listed"]);
Console.WriteLine(receipt.Proven ? "proven" : string.Join("; ", receipt.Tests.SelectMany(t => t.Reasons).Select(r => r.Message)));
```

A run recorded its specification identity when its OpenAPI or GraphQL schema coverage collector
loaded one: one aggregate coverage item per target carrying `spec.source` and the SHA-256 hash of the
loaded content. The identity item has no verdict, so report arithmetic ignores it.

## Limits

- `Verify` reads reports, not traces. Write them with a `ProtoTest.Reporting` sink
  (`JsonReportSink`); `ProtoReport.ReadJson` reads the sink's format.
- Coverage is the report's own arithmetic: units are coverage items with a verdict, matched by target,
  category and identifier. No spans are re-read and no coverage is recomputed.
- A regression is a unit the baseline covered and the candidate does not (`fail` by default); a new
  uncovered unit warns. A unit or target that disappeared from the candidate is not a finding, and the
  delta row shows it.
- Specification integrity compares the recorded hash with a candidate file you give. A remote source
  is recorded but never fetched; no candidate means recorded, not re-verified.
- The identity item records the configured source as written. An inline specification is therefore
  carried in the report metadata, so prefer a file or URL when report size matters.
- Failed candidate run gates are surfaced from the report's gate items; the candidate's own gate
  reasons stay its own. Verification does not add run gates and does not duplicate a suite's
  single-run coverage threshold.
- `Compare` matches tests by name and pairs operations by kind, name and subject in recorded order,
  with ports, ids and long numbers masked. A skipped test is not a failure.
  Error messages are not compared, only error types, because messages carry run-specific values.
- `Prove` proves what the recorded runs show: the claimed tests failed in the baseline and succeeded in
  every current run, no test broke, and the reports both runs embedded verify. Without embedded
  reports the receipt says coverage was not compared.
- The library writes nothing: no trace, no report, no rerun.

## Learn more

- [Coverage](https://prototest.dev/docs/observability/coverage)
- [Reporting](https://prototest.dev/docs/observability/reporting)
