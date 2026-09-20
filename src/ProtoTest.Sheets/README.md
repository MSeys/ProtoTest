# ProtoTest.Sheets

Open an `.xlsx` file produced by the application and assert on its cells, ranges, tables or typed rows.

```bash
dotnet add package ProtoTest.Sheets
```

```csharp
var workbook = Proto.Context.Sheets().Open("monthly-report.xlsx");

workbook.Sheet("Summary").Cell("B1").Should.Be(42.0);
workbook.Sheet("Sales").Model<SalesRow>().Verify();
```

Workbook reads and assertions are recorded in the trace and can contribute to Sheets coverage.

The package is read-only and supports OpenXML `.xlsx` files, not `.xls` or CSV.

## Learn more

- [Sheets integration](https://prototest.dev/docs/integrations/sheets/)
- [Download a report](https://prototest.dev/docs/recipes/download-a-report)
- [Sheets demo](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/SheetsJourney.cs)
