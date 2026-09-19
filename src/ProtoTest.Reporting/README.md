# ProtoTest.Reporting

Exports normalized ProtoTest observations and coverage as JSON or a self-contained HTML report at the end of a test run.

```bash
dotnet add package ProtoTest.Reporting --prerelease
```

```csharp
builder
    .AddSink<JsonReportSink>(sink => sink.OutputPath = "TestResults/prototest.json")
    .AddSink<HtmlReportSink>(sink => sink.OutputPath = "TestResults/prototest.html");
```

Report data is supplied by collectors implementing `IProtoReportSource`. See the [reporting guide](https://github.com/MSeys/ProtoTest/blob/main/docs/docs/observability/coverage.md).

The HTML report is fully self-contained and includes responsive light and dark themes,
summary metrics, hierarchical details, text search, and covered/partial/uncovered status filters. Press `/`
to focus search and `Escape` to clear it.
