---
sidebar_position: 4
title: Reporting
---

# Reporting

Report sinks write out what [collectors](./coverage.md) gathered, once, when the run ends. `ProtoTest.Reporting` ships two.

```bash
dotnet add package ProtoTest.Reporting
```

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

Both files are also added to the [`.prototrace` archive](./prototrace.md#the-file-format), so a single artifact from CI contains the trace *and* the reports.

## The HTML report

A single self-contained page: a summary (covered, uncovered, occurrences, findings, run gates, resources, errors) and every report item — endpoint → response → property for OpenAPI, type → field → argument for GraphQL — with covered, partially covered and uncovered paths marked.

Items are different things, so the report keeps them apart in sections: **Coverage** for what the contract exercises, **Findings** for evidence tests deliberately recorded, **Run gates** for run verdicts, labelled passed, advisory, failed or skipped, and **Resources** for what tests owned and released. An integration's own kind gets a section too, titled after the kind, rather than being dropped. Searching and filtering apply across all sections, and a section that filters to nothing disappears.

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

Property names match the .NET types (`ProtoReport`, `ProtoReportSummary`, `ProtoReportItem`) and enums are written as strings. The summary counts nested items too; `TotalOccurrences` counts observed hits only, so a finding or a gate verdict does not inflate it, and `CoveragePercentage` is rounded to two decimals and is `0` when there are no coverage items.

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

Two optional interfaces make a sink a first-class citizen:

- **`IProtoSinkArtifactSource`** — return the files you wrote from `GetArtifacts()` and they're added to the `.prototrace` archive.
- **`IProtoConfigurableOptions`** — expose a `ConfigurationSectionName` and your sink's public properties are bound from that section after the `AddSink` callback runs.

```csharp
public static IProtoHostBuilder AddSink<TSink>(this IProtoHostBuilder builder, Action<TSink>? configure = null) where TSink : class, IProtoSink;
public static IProtoHostBuilder AddSink<TSink>(this IProtoHostBuilder builder, TSink sink) where TSink : class, IProtoSink;
```

The first overload creates the sink through dependency injection, so it can take services. The second registers an instance as-is and **doesn't** apply configuration binding.

If a sink throws, the others still run and the failures are reported together.
