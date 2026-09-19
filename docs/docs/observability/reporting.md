---
sidebar_position: 3
title: Reporting
description: "Write collected observations and coverage out once per run, as JSON or as a self-contained HTML report."
---

# Reporting

Report sinks write out what [collectors](./coverage.md) gathered, once, when the run ends. `ProtoTest.Reporting` ships two.

## Install

```bash
dotnet add package ProtoTest.Reporting --prerelease
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

Both files are also added to the [`.prototrace` archive](./prototrace.md#the-file-format), under `resources/run/{SinkName}/run-artifact-{n}/{fileName}`, so a single artifact from CI contains the trace *and* the reports.

## The HTML report

A single self-contained page: a summary (covered, uncovered, occurrences, findings, run gates, resources, errors) and every report item — endpoint → response → property for OpenAPI, type → field → argument for GraphQL, service → method for gRPC — with covered, partially covered and uncovered paths marked.

Items are different things, so the report keeps them apart in sections: **Coverage** for what the contract exercises, **Findings** for evidence tests deliberately recorded, **Run gates** for run verdicts, labelled passed, warning, failed or skipped, and **Resources** for what tests owned and released. An integration's own kind gets a section too, titled after the kind, rather than being dropped. Searching and filtering apply across all sections, and a section that filters to nothing disappears.

Every coverage and observation row carries its own occurrence count. The summary's **Occurrences** card adds those per branch: each top-level coverage unit contributes its hit count once, and observations contribute their counts. Units nested under another unit — an OpenAPI response and its properties under the endpoint — are that unit's breakdown of the same calls, so they add nothing again; an aggregate row (a GraphQL type, `IsCovered` null) contributes nothing itself and does not hide the units below it. Findings and gate verdicts are recorded once rather than observed repeatedly, so they never inflate the count.

## The JSON report

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
  "Items": [ … ]
}
```

Property names match the .NET types (`ProtoReport`, `ProtoReportSummary`, `ProtoReportItem`) and enums are written as strings. `Total` counts nested items too. `TotalOccurrences` uses the per-branch rule above: coverage hit counts and observation counts, no double-counted nested breakdowns, no findings or gates. `CoveragePercentage` is rounded to two decimals and is `0` when there are no coverage items. Coverage totals count units only: an item whose `IsCovered` is `null` is an aggregate row, not a unit, so it stays out of `CoverageTotal`, `Covered`, `Uncovered` and `CoveragePercentage` — and a run gate's `CoverageSummaries` leaves it out too.

### What the snapshot sees

The report is a snapshot taken before the run's own resources are released: test-scoped resources have already been released when it is written, but a run-scoped resource (infrastructure, a container) still reads as registered and neutral there. Its release is recorded in the [ProtoTrace](./prototrace.md) afterwards.

### Teardown failures

A teardown failure never replaces the outcome the test reported — a failed assertion is not hidden by a cleanup error. The failing teardown operation is marked failed and the error is recorded as a **finding on the test's trace record**: status `Error`, category `Teardown`, target the test name, exception type as a tag. A passing test whose teardown failed reads as **Partial** rather than green. That finding lives on the trace record (and in the viewer), not in the report's Findings section, which counts findings tests recorded with `AddFinding` and items integrations report.

:::tip[A coverage gate]
A **run gate** checks the finished report and fails the run when it says no. Register one with `AddRunGate`; it can read coverage through `CoverageFor` / `CoverageSummaries`, so a threshold gate is a few lines rather than a CI script.
:::

## Configuring from files

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

## Writing a sink

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

Items arrive sorted by target, category and identifier. Only top-level items are passed — walk `Children` for nested ones.

A file-based sink can derive from `FileReportSink<TOptions>` instead of implementing `IProtoSink` by hand. It resolves the path, creates the directory, writes through your `WriteAsync`, and exposes the file as an artifact; a missing `OutputPath` throws naming the sink and its section:

```csharp
public abstract class FileReportSink<TOptions> : IProtoSink, IProtoSinkArtifactSource, IProtoConfigurableOptions
    where TOptions : class, IProtoFileReportOptions
{
    protected abstract string MediaType { get; }
    protected abstract string ArtifactDescription { get; }
    protected abstract Task WriteAsync(string outputPath, ProtoReport report, CancellationToken cancellationToken);
}
```

`IProtoFileReportOptions` is just `string OutputPath { get; set; }`; the sink's constructor takes the configuration section name, which is how both built-in sinks bind `ProtoTest:Reporting:Json` and `ProtoTest:Reporting:Html`.

Two optional interfaces make a sink a first-class citizen:

- **`IProtoSinkArtifactSource`** — return the files you wrote from `GetArtifacts()` and they're added to the `.prototrace` archive.
- **`IProtoConfigurableOptions`** — expose a `ConfigurationSectionName` and your sink's public properties are bound from that section after the `AddSink` callback runs.

```csharp
public static IProtoHostBuilder AddSink<TSink>(this IProtoHostBuilder builder, Action<TSink>? configure = null) where TSink : class, IProtoSink;
public static IProtoHostBuilder AddSink<TSink>(this IProtoHostBuilder builder, TSink sink) where TSink : class, IProtoSink;
```

The first overload creates the sink through dependency injection, so it can take services. The second registers an instance as-is and **doesn't** apply configuration binding. The same sink type is registered once however it was added; a repeated generic call appends its `configure` callback to that sink.

If a sink throws, the others still run; one failure is rethrown as-is, several as an `AggregateException("One or more report sinks failed to export.")`.

## Links

- [Coverage and observations](./coverage.md) defines what the items mean; [ProtoTrace](./prototrace.md) records the run and carries the report file.
- [Run gates](../foundation/lifecycle.md) and [configuration](../getting-started/configuration.md).
