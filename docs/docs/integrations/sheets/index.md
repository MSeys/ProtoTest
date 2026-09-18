---
sidebar_position: 6
title: Sheets
---

# Sheets

`ProtoTest.Sheets` opens the `.xlsx` your application generated and lets a test assert on its sheets, cells and ranges. It reads the file as OpenXML - the format itself - so it does not matter whether the application produced it with SpreadsheetGear, ClosedXML, EPPlus, NPOI, Aspose or raw OpenXML.

```bash
dotnet add package ProtoTest.Sheets
```

## Opening a workbook

```csharp
builder.AddSheets();

[ProtoTest]
public async Task TheMonthlyReport_ShouldMatch()
{
    using var response = await Proto.Context.Rest().GetAsync("/api/v1/reports/monthly.xlsx");
    var workbook = Proto.Context.Sheets().Open(await response.Content.ReadAsStreamAsync(), "monthly.xlsx");
}
```

`Open(string path)` reads from disk; `Open(Stream, name)` reads a captured attachment or response body. Hidden sheets are skipped unless `ProtoTest:Sheets:IncludeHiddenSheets` is set.

## Asserting

```csharp
var summary = workbook.Sheet("Summary");

summary.Cell("B1").ShouldBe(42.0);          // number, with an optional tolerance
summary.Cell("A1").ShouldBe("Total");       // text
summary.Cell("C1").ShouldBe(true);          // boolean
summary.Cell("A3").ShouldBe(reportDate);    // date, style-aware
summary.Cell("A2").Formula;                 // "SUM(B1:B1)", alongside the cached value

summary.Range("A4:B5").ShouldMatch(
[
    ["Region", "Amount"],
    ["EMEA", "1200"]
]);
```

Values are typed best-effort from the OpenXML cell type and number format. A missing sheet fails with the names that do exist; a reference outside the used range is an empty cell, not an error. Assertions are traced as `sheets.assert` operations with expected and actual values.

## Coverage

Reading a cell or range records a `sheets.range` observation, and the collector registered by `AddSheets` reports exactly the ranges the test read:

```csharp
var coverage = Proto.Context.Services
    .GetServices<IProtoCollector>()
    .OfType<SheetsCoverageCollector>()
    .Single()
    .GetReportItems();
```

Coverage therefore means "verified", not "present in the file".
