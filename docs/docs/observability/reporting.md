---
sidebar_position: 4
title: Reporting
description: "Write collected observations and coverage out once per run, as JSON or as a self-contained HTML report."
---

# Reporting

Report sinks write out what [collectors](./coverage.md) gathered, once, when the run ends. `ProtoTest.Reporting` ships two: a JSON report for tooling and a self-contained HTML report for people.

## What it is

A sink implements `IProtoSink` and receives the run's report items. `ProtoTest.Core` owns the interface; `ProtoTest.Reporting` ships the two built-in sinks.

```bash
dotnet add package ProtoTest.Reporting
```

The package targets .NET 8, 9 and 10 (the project template defaults to `net10.0`; pass `-f net8.0` or `net9.0` for an older runtime) and depends on `ProtoTest.Core` only.

```csharp
builder
    .AddSink<JsonReportSink>(sink => sink.OutputPath = "TestResults/report.json")
    .AddSink<HtmlReportSink>(sink =>
    {
        sink.OutputPath = "TestResults/report.html";
        sink.Title = "Northstar Platform";
    });
```

| Sink | Options | Defaults |
| --- | --- | --- |
| `JsonReportSink` | `OutputPath`, `Indented` | `TestResults/ProtoTest/report-{processId}.json`, `true` |
| `HtmlReportSink` | `OutputPath`, `Title` | `TestResults/ProtoTest/report-{processId}.html`, `"ProtoTest Report"` |

Both files are also added to the [`.prototrace` archive](./prototrace-archive.md#the-file-format) under `resources/run/{SinkName}/run-artifact-{n}/{fileName}`, so a single artifact from CI contains the trace and the reports.

Items arrive sorted by target, category and identifier. Only top-level items are passed; walk `Children` for nested ones.

## How to read it

### The HTML report

The HTML report is one self-contained page. It shows a summary and every report item. Covered, partial and uncovered paths are marked.

```text
Summary: totals · covered/uncovered · occurrences · findings · gates · resources
├── Coverage      endpoint → response → property (covered / partial / uncovered)
├── Findings      what tests recorded with AddFinding, plus what integrations reported
├── Run gates     verdicts: passed, warning, failed, skipped
├── Resources     what tests owned and released
└── Run metadata  the CI facts the run recorded about itself
search and filters apply across every section; an empty section hides itself
```

The report groups items into sections:

| Section | What it holds |
| --- | --- |
| **Coverage** | What the contract exercised |
| **Findings** | What tests recorded with `AddFinding`, plus what integrations reported |
| **Run gates** | Verdicts, labelled passed, warning, failed or skipped |
| **Resources** | What tests owned and released |
| **Run metadata** | The CI facts a run [recorded about itself](./prototrace-archive.md#correlating-a-trace-with-the-run-that-produced-it) |

An integration's own kind gets a section too, titled after the kind. Searching and filtering apply across all sections, and a section that filters to nothing disappears.

### Occurrences

Every coverage and observation row carries its own occurrence count. The summary's **Occurrences** card adds those per branch: each top-level coverage unit contributes its hit count once, and observations contribute their counts. Units nested under another unit (an OpenAPI response and its properties under the endpoint) are that unit's breakdown of the same calls, so they add nothing again. An aggregate row (a GraphQL type, `IsCovered` null) contributes nothing itself and does not hide the units below it. Findings and gate verdicts are recorded once rather than observed repeatedly, so they never inflate the count.

Worked through with numbers:

```text
GET /orders endpoint, 12 hits ............... +12 (top-level unit, counted once)
  ├── 200 response, 12 hits ................. +0 (nested breakdown of the same calls)
  ├── $.orderId property .................... +0 (nested breakdown)
  └── 404 response, 3 hits, uncovered ........ +0 (nested breakdown, even uncovered)
GraphQL type Order (aggregate, IsCovered null) +0 (contributes nothing itself)
invoice.state observation, seen 5 times ...... +5 (observations always count)
1 finding + 1 gate verdict .................. +0 (recorded once, never observed)
TotalOccurrences = 12 + 5 = 17
```

### The JSON report

The same data, for tooling:

```json
{
  "GeneratedAtUtc": "2026-09-17T08:12:44+00:00",
  "Summary": {
    "Total": 214,
    "TotalOccurrences": 1893,
    "CoverageTotal": 198,
    "Covered": 151,
    "Uncovered": 47,
    "CoveragePercentage": 76.26,
    "Warnings": 0,
    "Errors": 0,
    "Findings": 2,
    "Gates": 1,
    "Resources": 37
  },
  "Items": [
    {
      "TargetName": "Northstar:Api",
      "Category": "OpenAPI",
      "Identifier": "GET /api/v1/orders",
      "Kind": "coverage",
      "Status": "Success",
      "Count": 12,
      "IsCovered": true,
      "Value": null,
      "Unit": null,
      "Message": null,
      "Tags": null,
      "Children": [ { "TargetName": "Northstar:Api", "Category": "OpenAPI Response", "Identifier": "200", "Kind": "coverage", "Status": "Success", "Count": 12, "IsCovered": true } ],
      "Metadata": null,
      "DisplayName": null,
      "DisplayGroup": null
    }
  ]
}
```

Every property of `ProtoReportItem` is written, so an unset one is present with a `null` value. The responses and properties of that endpoint are nested under `Children`; the row above is the endpoint and its `200` response, abridged to the fields the example is about.

Property names match the .NET types (`ProtoReport`, `ProtoReportSummary`, `ProtoReportItem`) and enums are written as strings. `Total` counts nested items too. `TotalOccurrences` uses the per-branch rule above: coverage hit counts and observation counts, no double-counted nested breakdowns, no findings or gates. `CoveragePercentage` is rounded to two decimals and is `0` when there are no coverage items. Coverage totals count units only: an item whose `IsCovered` is `null` is an aggregate row, not a unit, so it stays out of `CoverageTotal`, `Covered`, `Uncovered` and `CoveragePercentage`. A run gate's `CoverageSummaries` leaves it out too.

## The artifact

The artifact is the file a sink writes. A relative `OutputPath` resolves against the process working directory. That is the test project's build output folder. A missing `OutputPath` throws when the sink exports, naming the sink and its configuration section.

### Configuring from files

Both sinks read configuration sections, so CI can change paths without code changes:

```json
{
  "ProtoTest": {
    "Reporting": {
      "Json": { "OutputPath": "artifacts/report.json", "Indented": false },
      "Html": { "OutputPath": "artifacts/report.html", "Title": "Nightly" }
    }
  }
}
```

See [Configuration](../getting-started/configuration.md) for which source wins.

### Writing a sink

```csharp
public interface IProtoSink
{
    Task ExportAsync(IEnumerable<ProtoReportItem> items, CancellationToken cancellationToken = default);
}
```

```csharp
public sealed class ConsoleSummarySink : IProtoSink
{
    public Task ExportAsync(IEnumerable<ProtoReportItem> items, CancellationToken cancellationToken = default)
    {
        var coverage = items.Where(item => item.Kind == ProtoReportItemKinds.Coverage).ToList();
        var uncovered = coverage.Where(item => item.IsCovered == false).ToList();

        Console.WriteLine($"Coverage: {coverage.Count - uncovered.Count}/{coverage.Count}");
        foreach (var item in uncovered)
            Console.WriteLine($"  never exercised: [{item.Category}] {item.Identifier}");

        return Task.CompletedTask;
    }
}
```

```csharp
builder.AddSink<ConsoleSummarySink>();
```

A file-based sink can derive from `FileReportSink<TOptions>` instead of implementing `IProtoSink` by hand. It resolves the path, creates the directory, writes through your `WriteAsync`, and exposes the file as an artifact. A missing `OutputPath` throws as described above.

```csharp
public abstract class FileReportSink<TOptions> : IProtoSink, IProtoSinkArtifactSource, IProtoConfigurableOptions
    where TOptions : class, IProtoFileReportOptions
{
    protected abstract string MediaType { get; }
    protected abstract string ArtifactDescription { get; }
    protected abstract Task WriteAsync(string outputPath, ProtoReport report, CancellationToken cancellationToken);
}
```

`IProtoFileReportOptions` is just `string OutputPath { get; set; }`. The sink's constructor takes the configuration section name, which is how both built-in sinks bind `ProtoTest:Reporting:Json` and `ProtoTest:Reporting:Html`.

Two optional interfaces make a sink a first-class citizen:

- **`IProtoSinkArtifactSource`**: return the files you wrote from `GetArtifacts()` and they are added to the `.prototrace` archive.
- **`IProtoConfigurableOptions`**: expose a `ConfigurationSectionName` and your sink's public properties are bound from that section after the `AddSink` callback runs.

`AddSink<TSink>()` takes an optional configure callback and creates the sink through dependency injection, so it can take services. `AddSink<TSink>(sink)` registers an instance as-is and does not apply configuration binding. The same sink type is registered once however it was added, and a repeated generic call appends its `configure` callback to that sink.

## Limits

Snapshot timing (what the report can and cannot show):

- A teardown failure never replaces the outcome the test reported. The failing teardown operation is marked failed, and the error becomes a **finding on the test's trace record**: status `Error`, category `Teardown`, target the test name, exception type as a tag. A passing test whose teardown failed reads as **Partial** rather than green in the trace; the runner still reports the test itself as passed. That finding travels the same `AddFinding` path as any other finding, so it appears in the report's Findings section, counts in the summary, and run gates see it, as well as living on the trace record and in the viewer.
- The report is a snapshot taken before the run's own resources are released. Test-scoped resources have already been released when it is written, but a run-scoped resource (infrastructure, a container) still reads as registered and neutral there. Its release is recorded in the [ProtoTrace](./prototrace.md) afterwards.

Sink failures (what happens when writing fails):

- If a sink throws, the others still run. One failure is rethrown as-is; several become an `AggregateException("One or more report sinks failed to export.")`.
- The built-in sinks write one report per run. They do not append to a previous report or keep a history.

## Learn more

- [Coverage and observations](./coverage.md): what the items mean.
- [ProtoTrace](./prototrace.md): the run record that carries the report file.
- [Run gates](../foundation/lifecycle.md) and [configuration](../getting-started/configuration.md).
