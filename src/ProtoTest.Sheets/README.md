# ProtoTest.Sheets

Spreadsheet testing for ProtoTest: open the `.xlsx` your application generated and assert on sheets, cells and ranges, with coverage.

```bash
dotnet add package ProtoTest.Sheets --prerelease
```

```csharp
builder.AddSheets();

[ProtoTest]
public async Task TheMonthlyReport_ShouldMatch()
{
    using var response = await Proto.Context.Rest().GetAsync("/api/v1/reports/monthly.xlsx");
    var workbook = Proto.Context.Sheets().Open(response);   // the file name comes from the response

    workbook.Sheet("Summary").Cell("B1").ShouldBe(42.0);
    workbook.Sheet("Summary").Range("A4:B5").ShouldMatch(
    [
        ["Region", "Amount"],
        ["EMEA", "1200"]
    ]);
}
```

- Any producer works - SpreadsheetGear, ClosedXML, EPPlus, NPOI, Aspose or raw OpenXML - because the file is read as the OpenXML standard, never through the library that wrote it.
- Values are typed best-effort: text, number, boolean and date (style-aware), with the formula text kept alongside its cached result.
- Reading a cell or range records a `sheets.range` observation, and building a `Table(...)` view records the whole used data range it covers; the built-in `SheetsCoverageCollector` reports the ranges the test opened.
- See the [spreadsheets guide](https://github.com/MSeys/ProtoTest/blob/main/docs/docs/integrations/sheets/index.md).
