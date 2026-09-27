# ProtoTest.Sheets

Open an `.xlsx` file produced by the application and assert on its cells, ranges, tables, typed rows or label/value blocks.

```bash
dotnet add package ProtoTest.Sheets
```

```csharp
var workbook = Proto.Context.Sheets().Open("monthly-report.xlsx");

workbook.Model<SalesRow>().Should.MatchHeaders().Should.MatchModel();
workbook.KeyValueModel<SummarySheet>().Column(s => s.Total).Should.Be(123.45m);
workbook.Sheet("Notes").Cell("B1").Should.Be("checked");
```

Workbook reads and assertions are recorded in the trace; cell and range reads contribute to Sheets
coverage, while opening a workbook is recorded as `sheets.workbook` evidence and covers nothing.

The package is read-only and supports OpenXML `.xlsx` files, not `.xls` or CSV.

## Learn more

- [Sheets integration](https://prototest.dev/docs/integrations/sheets/)
- [Download a report](https://prototest.dev/docs/recipes/download-a-report)
- [Sheets demo](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/SheetsJourney.cs)
