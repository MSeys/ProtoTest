# ProtoTest.Reporting

Writes report items collected during a run to JSON or a self-contained HTML file.

```bash
dotnet add package ProtoTest.Reporting
```

```csharp
builder
    .AddSink<JsonReportSink>(sink => sink.OutputPath = "TestResults/report.json")
    .AddSink<HtmlReportSink>(sink => sink.OutputPath = "TestResults/report.html");
```

Reports are written once at the end of the run. Coverage collectors and other report sources provide the items included in them.

## Learn more

- [Reporting](https://prototest.dev/docs/observability/reporting)
- [Coverage](https://prototest.dev/docs/observability/coverage)
- [Demo setup](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/Setup.cs)
