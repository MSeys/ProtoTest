---
sidebar_position: 4
title: Reporting
description: "Write collected observations and coverage out once per run, as JSON or as a self-contained HTML report."
---

import Screenshot from '@site/src/components/Screenshot';

# Reporting

Report sinks write out what [collectors](./coverage.md) gathered, once, when the run ends. `ProtoTest.Reporting` ships two: a JSON report for tooling and a self-contained HTML report for people.

## What it is

A sink implements `IProtoSink` and receives the run's report items. `ProtoTest.Core` owns the interface, and `ProtoTest.Reporting` ships the two built-in sinks.

```bash
dotnet add package ProtoTest.Reporting
```

The package targets .NET 8, 9 and 10 and depends on `ProtoTest.Core` only. The project template defaults to `net10.0`. Pass `--framework net8.0` or `--framework net9.0` for an older runtime.

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

The template and the sample set their own paths, so what a reader sees differs from the defaults by design. The template writes `TestResults/Shop.json` and `TestResults/Shop.html`, and the sample writes `report.*` under its output folder.

Items arrive sorted by target, category and identifier. Only top-level items are passed, so walk `Children` for nested ones.

## How to read it

### The HTML report

The HTML report is one self-contained page, laid out like the run view of the [ProtoTrace viewer](./prototrace.md):

<Screenshot
  name="docs/reporting/report"
  alt="The top of a ProtoTest HTML report: the headline 255 uncovered, 1 finding, 46 of 301 units covered; the meta line; a strip of ticks; tabs Needs attention, Gates, Findings, Coverage, Traffic, Resources and All; and the first entries that need attention."
  caption={<span>The report the Northstar suite wrote with its drills on, from the viewer's <a href="https://trace.prototest.dev">demo trace</a>: the headline, the meta line, the strip and one tab per kind.</span>}
  width={860}
  height={501}
/>

The headline names failed gates, errors, uncovered units, findings and warnings, then the coverage figure when the run measured coverage. A run where nothing needs attention says so. Each tick in the strip is one entry; the ones that failed or warned stand up, and a click opens that entry.

**Needs attention** lists every entry that failed, warned or left a coverage gap, across all kinds, with its first message line. A row opens the entry in its own tab. The other tabs hold one kind each:

| Tab | What it holds |
| --- | --- |
| **Gates** | Run gate verdicts, labelled passed, warning, failed or skipped |
| **Findings** | What tests recorded with `AddFinding`, plus what integrations reported |
| **Coverage** | What the contract exercised, endpoint to response to property, with the covered figure and a bar |
| **Traffic** | What was observed but not asserted |
| **Observations** | What tests and integrations observed, with counts |
| **Metrics** | Measured values |
| **Resources** | What tests owned and released |
| **Run metadata** | The CI facts a run [recorded about itself](./prototrace-archive.md#correlating-a-trace-with-the-run-that-produced-it) |

An integration's own kind gets a tab too, titled after the kind. A kind with no entries has no tab. Search (`/`) looks through every kind at once.

The report uses the viewer's tokens, type and colours, in a dark and a light theme.

A quiet footer points at the ProtoTrace viewer: drop the run's `.prototrace` file at `https://trace.prototest.dev` for the full trace.

### Occurrences

Every coverage and observation row carries its own occurrence count. The meta line's **observed occurrences** adds those per branch. Each top-level coverage unit contributes its hit count once, and observations contribute their counts. Units nested under another unit (an OpenAPI response and its properties under the endpoint) are that unit's breakdown of the same calls, so they add nothing again. An aggregate row (a GraphQL type, `IsCovered` null) contributes nothing itself and does not hide the units below it. Findings and gate verdicts are recorded once rather than observed repeatedly, so they never inflate the count.

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
    "Resources": 37,
    "Kinds": { "coverage": 198, "finding": 2, "gate": 1, "observation": 0, "resource": 37 }
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

Every property of `ProtoReportItem` is written, so an unset one is present with a `null` value. The responses and properties of that endpoint are nested under `Children`. The row above is the endpoint and its `200` response, abridged to the fields the example is about.

Property names match the .NET types (`ProtoReport`, `ProtoReportSummary`, `ProtoReportItem`) and enums are written as strings. `Total` counts nested items too. `TotalOccurrences` uses the per-branch rule above. It adds coverage hit counts and observation counts, with no double-counted nested breakdowns and no findings or gates. `Kinds` counts the items of each kind, nested ones included, keyed by the lower-case kind; a report written before 1.1 reads with an empty `Kinds`. `CoveragePercentage` is rounded to two decimals and is `0` when there are no coverage items. Coverage totals count units only. An item whose `IsCovered` is `null` is an aggregate row, not a unit, so it stays out of `CoverageTotal`, `Covered`, `Uncovered` and `CoveragePercentage`. A run gate's `CoverageSummaries` leaves it out too.

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

`IProtoFileReportOptions` holds only `string OutputPath { get; set; }`. The sink's constructor takes the configuration section name, which is how both built-in sinks bind `ProtoTest:Reporting:Json` and `ProtoTest:Reporting:Html`.

Two optional interfaces make a sink a first-class citizen:

- **`IProtoSinkArtifactSource`**: return the files you wrote from `GetArtifacts()` and they are added to the `.prototrace` archive.
- **`IProtoConfigurableOptions`**: expose a `ConfigurationSectionName` and your sink's public properties are bound from that section after the `AddSink` callback runs.

`AddSink<TSink>()` takes an optional configure callback and creates the sink through dependency injection, so it can take services. `AddSink<TSink>(sink)` registers an instance as-is and does not apply configuration binding. The same sink type is registered once however it was added, and a repeated generic call appends its `configure` callback to that sink.

## Limits

Snapshot timing (what the report can and cannot show):

- A teardown failure never replaces the outcome the test reported. The failing teardown operation is marked failed, and the error becomes a **finding on the test's trace record**: status `Error`, category `Teardown`, target the test name, exception type as a tag.
- A passing test whose teardown failed reads as **Partial** rather than green in the trace. The runner still reports the test itself as passed.
- That finding travels the same `AddFinding` path as any other finding. It appears in the report's Findings section, counts in the summary and is seen by run gates, as well as on the trace record and in the viewer.
- The report is a snapshot taken before the run's own resources are released. Test-scoped resources have already been released when it is written, but a run-scoped resource (infrastructure, a container) still reads as registered and neutral there. Its release is recorded in the [ProtoTrace](./prototrace.md) afterwards.

Sink failures (what happens when writing fails):

- If a sink throws, the others still run. One failure is rethrown as-is, and several become an `AggregateException("One or more report sinks failed to export.")`.
- The built-in sinks write one report per run. They do not append to a previous report or keep a history.

## Learn more

- [Coverage and observations](./coverage.md): what the items mean.
- [ProtoTrace](./prototrace.md): the run record that carries the report file.
- [Run gates](../foundation/lifecycle.md#run-gates-and-resources) and [configuration](../getting-started/configuration.md).
