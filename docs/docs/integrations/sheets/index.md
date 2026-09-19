---
sidebar_position: 11
title: Sheets
description: "Open the .xlsx your application generated and assert on its sheets, cells, ranges and typed rows."
---

# Sheets

`ProtoTest.Sheets` opens the `.xlsx` your application generated and lets a test assert on its sheets, cells and ranges. It reads the file as OpenXML — the format itself — so it does not matter whether the application produced it with SpreadsheetGear, ClosedXML, EPPlus, NPOI, Aspose or raw OpenXML.

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
    var workbook = Proto.Context.Sheets().Open(response);   // the file name comes from the response
}
```

`Open(string path)` reads from disk; `Open(Stream, name)` reads a stream; `Open(IProtoBinaryContent)` reads anything that carries named bytes — a REST response, a captured attachment — so downloading and checking a report reads as one line.

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

Values are typed best-effort from the OpenXML cell type and number format. A missing sheet fails with the names that do exist; a reference outside the used range is an empty cell, not an error. Assertions are traced as `assert.sheets` operations with expected and actual values.

## Typed sheet models

For a sheet that is really a table — a header row, then one record per row — describe the row once as a record and let the header paths bind the columns:

```csharp
[Sheet("Sales", HeaderRows = [1, 2])]
public sealed record SalesRow(
    [property: Column("Region", Pattern = "^[A-Z]+$", Unique = true)] string Region,
    [property: Column("FY26", "Amount", Min = 0)] decimal Amount,
    [property: Column("FY26", "Count", Min = 0)] int Count);
```

`[Sheet]` names the worksheet and its header rows — two here, a merged "FY26" group over "Amount" and "Count". `[Column]` binds a property to a header path and can carry the rules every value in that column must meet: `Min` and `Max` for numbers, `Pattern` and `OneOf` for text, `Unique`, and `Optional` for columns that may be empty (the property then needs a nullable type).

```csharp
var sales = Proto.Context.Sheets().Open(response).Model<SalesRow>();

sales.Verify();                                                    // every rule, every row
sales.Column(row => row.Amount).ShouldBeSortedBy(ascending: false);
sales.Column(row => row.Amount).ShouldAll(amount => amount > 0);
sales.Row(row => row.Region == "EMEA")
    .ShouldMatchShape(new { Amount = 1200m, Count = 12 });
```

- `Model<TRow>()` fails straight away when a declared header is missing from the sheet, naming it.
- `Verify()` checks every declared column against its rules and reports **every** violation in one failure, with the cell reference of each.
- `Rows` and `Row(predicate)` give typed records; `Column(row => row.Amount)` reads a whole column with `ShouldBe`, `ShouldAll` and `ShouldBeSortedBy`.
- A row is matched with the same [shapes](../../foundation/shape-matching.md) as a JSON response.

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
