# ProtoTest.Sheets

Spreadsheet testing: open the `.xlsx` your application generated and assert on cells, ranges, tables and typed models, with range coverage.

```bash
dotnet add package ProtoTest.Sheets
```

## Quick start

```csharp
builder.AddSheets();

using var response = await Proto.Context.Rest().GetAsync("/api/v1/reports/monthly.xlsx");
var workbook = Proto.Context.Sheets().Open(response);   // the file name comes from the response

workbook.Sheet("Summary").Cell("B1").Should.Be(42.0);
workbook.Sheet("Summary").Range("A4:B5").Should.Match(
[
    ["Region", "Amount"],
    ["EMEA", "1200"]
]);
```

## What it adds

- **Opening** — `Proto.Context.Sheets()` plus `Open(path)`, `Open(stream, name)` and `Open(IProtoBinaryContent)`; reading is eager and uses OpenXML, never the library that wrote the file.
- **Reading** — `ProtoWorkbook.Sheets`, `Sheet(name)`, `Cell(reference)`/`Cell(row, column)`, `Range(reference)`, `Table(headerRows)` and `Model<TRow>()` with `[Sheet]`/`[Column]` attributes.
- **Assertions** — every object exposes `Should` and `ShouldNot`; `Cell.Should.Be/BeText/BeBlank/HaveFormula`, `Range.Should.Match`/`HaveDimensions`, `Table.Should.ContainRow`, typed columns `Be`/`BeSortedBy`/`ShouldAll`, and model `Verify()`.
- **Coverage** — reads record `sheets.range` and workbook open records `sheets.workbook`; the built-in `SheetsCoverageCollector` reports them, so coverage means verified, not present.
- **Tracing** — `sheets.open` with a sheet summary, `sheets.model` with the chosen columns, and `assert.sheets` for every assertion.

## Configuration

| Key | Type | Default |
| --- | --- | --- |
| `ProtoTest:Sheets:IncludeHiddenSheets` | `bool` | `false` |

OpenXML `.xlsx` only, read-only: no writing and no `.xls`/CSV. Range reads are capped at 1,000,000 cells, date detection is a style heuristic, and formula cells expose the cached value.

## Learn more

- [Sheets guide](https://prototest.dev/docs/integrations/sheets/)
- [SheetsJourney.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/SheetsJourney.cs)
