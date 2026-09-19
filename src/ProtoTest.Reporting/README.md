# ProtoTest.Reporting

Exports the report items a run collected — coverage and the other report sources — as JSON or a self-contained HTML report.

```bash
dotnet add package ProtoTest.Reporting
```

## Quick start

```csharp
builder
    .AddSink<JsonReportSink>(sink => sink.OutputPath = "TestResults/prototest.json")
    .AddSink<HtmlReportSink>(sink =>
    {
        sink.OutputPath = "TestResults/prototest.html";
        sink.Title = "Northstar Platform";
    });
```

## What it adds

- **Sinks** — `AddSink<JsonReportSink>()` and `AddSink<HtmlReportSink>()` on the host builder, plus an instance overload for a prebuilt sink.
- **Output** — `OutputPath` (and `Indented` for JSON, `Title` for HTML); a missing `OutputPath` throws when the sink exports.
- **Base type** — `FileReportSink<TOptions>` with protected `MediaType`, `ArtifactDescription` and `WriteAsync` for your own file sink.
- **Lifecycle** — export runs once at the end of the run, after run gates and before run-scoped resources are released; the output file becomes a run artifact in `.prototrace`.
- **Input** — items come from collectors that implement `IProtoReportSource` and from `IProtoReportSource` registrations, deduplicated and sorted; observations are not a report source by themselves.

## Configuration

| Key | Type | Default |
| --- | --- | --- |
| `ProtoTest:Reporting:Json:OutputPath` | `string` | `TestResults/ProtoTest/report-{pid}.json` |
| `ProtoTest:Reporting:Json:Indented` | `bool` | `true` |
| `ProtoTest:Reporting:Html:OutputPath` | `string` | `TestResults/ProtoTest/report-{pid}.html` |
| `ProtoTest:Reporting:Html:Title` | `string` | `ProtoTest Report` |

The instance overload deliberately ignores configuration binding, and the report is a snapshot written before run-scoped resources are released, so a container still reads as registered.

## Learn more

- [Reporting guide](https://prototest.dev/docs/observability/reporting)
- [Demo sink configuration](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/Setup.cs)
