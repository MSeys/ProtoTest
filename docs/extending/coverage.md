# Observations, coverage, and reporting

ProtoTest Core records generic `ProtoObservation` values. Collectors consume those observations; collectors that also implement `IProtoReportSource` can expose normalized `ProtoReportItem` values to sinks. Coverage is one useful projection, not a restriction of the collection model.

For a complete runnable configuration, see the [unified control-plane demo](../examples/sample-app.md). It registers REST, OpenAPI, and GraphQL collectors and writes both report formats at suite teardown.

## Use an existing coverage collector

Integrations expose their collectors through the shared target builder:

```csharp
builder.AddRest(rest => rest
    .AddClient("Orders")
    .WithCollector<RestCoverageCollector>());
```

For contract coverage, use `OpenApiCoverageCollector` in the same way. A target can have multiple collectors, including collectors that do not produce reports.

## Record an observation

Tests and integrations can publish domain data without depending on reporting:

```csharp
Proto.Context.RecordObservation(
    targetName: "Orders",
    kind: "http.response",
    identifier: "GET /orders/{id}",
    data: new RestResponseData(
        Method: "GET",
        RouteTemplate: "/orders/{id}",
        StatusCode: 200,
        ResponseBody: "{\"id\":42}",
        Headers: new Dictionary<string, string>()));

var observations = Proto.Context.RecordedObservations;
```

REST request builders and shape matching already record their observations automatically.

## Build a collector

Implement `IProtoCollector` when an integration only needs to consume information:

```csharp
public sealed class TelemetryCollector(string targetName) : IProtoCollector
{
    public bool CanCollect(ProtoObservation observation) =>
        string.Equals(observation.TargetName, targetName, StringComparison.OrdinalIgnoreCase);

    public void Collect(ProtoObservation observation)
    {
        // Forward, aggregate, or inspect the observation.
    }
}
```

Register it with `.WithCollector<TCollector>()`. Collectors should support concurrent calls because tests and requests can execute in parallel.

If a collector also has normalized output, implement `IProtoReportSource`. For standard covered/uncovered aggregation, derive from `ProtoCoverageCollector` and override `Category`, `Collect`, or `GetReportItems` as needed. A standalone service may implement only `IProtoReportSource` when it already owns reportable data.

## Export reports at the end of a run

Install `ProtoTest.Reporting` for the built-in JSON and HTML sinks:

```csharp
builder
    .AddSink<JsonReportSink>()
    .AddSink<HtmlReportSink>();
```

Configure concrete sinks in code:

```csharp
builder
    .AddSink<JsonReportSink>(sink => sink.OutputPath = "artifacts/report.json")
    .AddSink<HtmlReportSink>(sink =>
    {
        sink.OutputPath = "artifacts/report.html";
        sink.Title = "Orders API report";
    });
```

`AddSink` and the end-of-run orchestration live in Core. Every registered sink is attempted, and multiple failures are aggregated. Custom sinks only implement `IProtoSink`:

```csharp
builder.AddSink<MySink>();
```

The fixed `ProtoTest:Reporting:Json` and `ProtoTest:Reporting:Html` configuration sections override code defaults:

```json
{
  "ProtoTest": {
    "Reporting": {
      "Json": { "OutputPath": "artifacts/report.json", "Indented": true },
      "Html": { "OutputPath": "artifacts/report.html", "Title": "API report" }
    }
  }
}
```

Without configured paths, filenames include the process ID so parallel test assemblies do not overwrite each other.

The HTML sink produces a self-contained, responsive report. It offers hierarchical
details, light and dark themes, text search, and filters for covered, partial,
uncovered, warning, and error groups. Parent rows are marked partial whenever their
coverage subtree contains both covered and uncovered entries. Press `/` to focus the
search field and `Escape` to clear it.

## Semantic report data

Use `ProtoReportItem.Kind`, `Status`, `Message`, `Tags`, `Value`, `Unit`, and `Metadata` to communicate meaning to sinks:

```csharp
new ProtoReportItem(
    TargetName: "Orders",
    Category: "Contract",
    Identifier: "Deprecated response field",
    Kind: ProtoReportItemKind.Finding,
    Status: ProtoReportStatus.Warning,
    Message: "Remove before v2",
    Tags: ["deprecated", "contract"]);
```

Avoid presentation attributes on arbitrary payload properties. Collectors or report sources translate domain data into semantic items; sinks own colors and layout.
