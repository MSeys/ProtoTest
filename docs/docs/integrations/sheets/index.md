---
sidebar_position: 11
title: Sheets
description: "Open the .xlsx your application generated and assert on its sheets, cells, ranges and typed rows."
---

# Sheets

`ProtoTest.Sheets` opens the `.xlsx` your application generated and lets a test assert on its sheets, cells, ranges and typed rows. It reads the file as OpenXML — the format itself — so it does not matter whether the application produced it with SpreadsheetGear, ClosedXML, EPPlus, NPOI, Aspose or raw OpenXML.

Reading is eager and complete: a missing sheet, a malformed reference or a reversed range fails immediately instead of surfacing later. A reference outside the used range is an empty cell, not an error.

```csharp
builder.AddSheets();

using var response = await Proto.Context.Rest().GetAsync("/api/v1/reports/monthly.xlsx");
var workbook = Proto.Context.Sheets().Open(response);       // the file name comes from the response

workbook.Sheet("Summary").Cell("B1").Should.Be(42.0);
```

## Install

```bash
dotnet add package ProtoTest.Sheets --prerelease
```

ProtoTest targets .NET 8, 9 and 10; the template defaults to `net10.0` unless `-f` is passed. The package builds on OpenXML and brings `ProtoTest.Json` with it for row shape matching.

## Registering

```csharp
public static IProtoHostBuilder AddSheets(
    this IProtoHostBuilder builder,
    Action<SheetsOptions>? configure = null);
```

`AddSheets` registers the `Sheets` capability (`ProtoCapabilityKinds.Document`), a singleton `SheetsOptions` built from the callback and then bound from `ProtoTest:Sheets`, and the `SheetsCoverageCollector`. Repeats are no-ops: the first options callback wins, the first collector instance wins, and the capability descriptor dedupes.

## Options and keys

| Key | Option | Type | Default |
| --- | --- | --- | --- |
| `ProtoTest:Sheets:IncludeHiddenSheets` | `SheetsOptions.IncludeHiddenSheets` | `bool` | `false` |

When false, hidden sheets are omitted from `ProtoWorkbook.Sheets` and from every count derived from it; each sheet still carries its original position in `ProtoSheet.Index`. Configuration layers over the code callback.

## Context API

```csharp
ProtoSheets sheets = Proto.Context.Sheets();
```

Calling it without `AddSheets` throws `InvalidOperationException` with the guidance to call `AddSheets` on the host builder.

| Open overload | Source |
| --- | --- |
| `Open(string path)` | a file on disk; the workbook name is the file name |
| `Open(Stream stream, string name = "workbook.xlsx")` | an in-memory stream |
| `Open(IProtoBinaryContent content, string? name = null)` | anything carrying named bytes — a REST response, a captured attachment; the name falls back to `content.FileName`, then `workbook.xlsx` |

On the opened workbook:

| Member | |
| --- | --- |
| `Name`, `Sheets` | the workbook name and the sheets that were read (visible ones by default) |
| `Sheet(name)` | finds a sheet; a failure lists the available names |
| `Model<TRow>()` | binds a `[Sheet]`/`[Column]` record to the workbook |

On a sheet: `Cell(reference)`, `Cell(row, column)` (1-based), `Range(reference)`, `Table(params int[] headerRows)` (defaults to row 1), plus `Name`, `Index`, `IsHidden`, `RowCount` and `ColumnCount`.

## Quick start

```csharp
var summary = Proto.Context.Sheets().Open(response).Sheet("Summary");

summary.Cell("A1").Should.Be("Total");
summary.Cell("B1").Should.Be(42.0);
summary.Cell("C1").Should.Be(true);
summary.Cell("A3").Should.Be(reportDate);
summary.Cell("A2").Should.HaveFormula("SUM(B1:B1)");

summary.Range("A4:B5").Should.Match(
[
    ["Region", "Amount"],
    ["EMEA", "1200"]
]);
```

Values are typed best-effort from the OpenXML cell type and number format. A cell's `Formula` text is kept alongside its cached value; formulas are never recalculated.

## Going further

### Cells

Every assertion object exposes `Should` (positive) and `ShouldNot` (negated) facades whose polarity is fixed by the property, so a negative failure reads "Expected … not to …" with the same evidence.

| Cell assertion | |
| --- | --- |
| `Be(expected)` | compares text, number, boolean or date; numbers within `1e-6`, dates within a second; `null` means "holds no text"; an unsupported expected type throws `ArgumentException` |
| `BeText()` | the cell holds text |
| `BeBlank()` | the cell is empty |
| `HaveFormula(formula)` | the formula text matches exactly |

### Ranges

`Range(reference).Should.Match(expected)` compares rendered cell values row by row, ordinal, and checks the shape first: a dimension mismatch fails before any value comparison. `Should.HaveDimensions(rows, columns)` checks the shape without reading values. `ShouldNot.Match(...)` passes only when the range differs. Reversed ranges (`B5:A1`) and ranges over 1,000,000 cells are rejected outright.

### Tables

A table is a header-aware view of a sheet area; header rows can be layered, with a merged group header over subheaders:

```csharp
var table = workbook.Sheet("Sales").Table(1, 2);      // header rows 1 and 2
var amounts = table.Column("FY26", "Amount");         // full header path
table.Should.ContainRow("Region", "EMEA");            // one rendered value match
var emea = table.RowWhere("Region", "EMEA");          // throws when no row matches
string amount = emea["FY26", "Amount"].Text;          // row indexer by header path
```

A column is found by its full header path; a single segment may match by suffix when it is unambiguous. Zero matches and more than one full or suffix match throw `SpreadsheetAssertionException` naming the candidate paths, so ambiguity fails instead of guessing. Header matching is ordinal (case-sensitive), and a single-segment path has no case folding. `ContainRow` compares rendered values, so a numeric or date key cell matches its printed form.

### Typed models

For a sheet that is really a table, describe the row once as a record and let header paths bind the columns:

```csharp
[Sheet("Sales", HeaderRows = [1, 2])]
public sealed record SalesRow(
    [property: Column("Region", Pattern = "^[A-Z]+$", Unique = true)] string Region,
    [property: Column("FY26", "Amount", Min = 0)] decimal Amount,
    [property: Column("FY26", "Count", Min = 0)] int Count);

var sales = Proto.Context.Sheets().Open(response).Model<SalesRow>();

sales.Verify();                                                    // every rule, every row
sales.Column(row => row.Amount).Should.Be([1200m, 900m]);
sales.Column(row => row.Amount).Should.BeSortedBy(ascending: false);
sales.Column(row => row.Amount).ShouldAll(amount => amount > 0);
sales.Row(row => row.Region == "EMEA")
    .ShouldMatchShape(new { Amount = 1200m, Count = 12 });
```

`[Sheet(name)]` names the worksheet and its `HeaderRows` (default `[1]`). `[Column(path)]` binds a property to a header path and can carry `Optional`, `Min`, `Max`, `Pattern`, `OneOf` and `Unique`. `Model<TRow>()` requires `[Sheet]`, rejects a model with no `[Column]` properties, and rejects `Optional` on a non-nullable property; a missing header fails immediately with the names the sheet has.

- `Rows` returns the projected records; `Row(predicate)` fails when nothing matches; `Column(row => row.Amount)` reads a typed column.
- Typed columns support `string`, `decimal`, `double`, `int`, `long`, `bool`, `DateTime` and their nullables. `Should.Be` uses `EqualityComparer<TValue?>.Default`, `Should.BeSortedBy` uses `Comparer<TValue?>.Default`, and `ShouldAll(predicate)` is positive-only and reports the first failing row.
- `Verify()` checks every declared column and reports **all** violations in one failure — emptiness and non-nullability, conversion, `Min`/`Max` (numbers and date serial values), `Pattern`, `OneOf`, and `Unique` with kind-aware keys. The message shows up to ten, then `+N more`.
- A row is matched with the same [shapes](../../foundation/shape-matching.md) as a JSON response, through `row.ShouldMatchShape(shape)`.
- Records are populated without running their constructors; an optional empty cell binds as `null`.

### Hidden sheets

Hidden sheets are skipped unless `ProtoTest:Sheets:IncludeHiddenSheets` (or the option callback) turns them on. `ProtoSheet.Index` always reflects the position in the workbook including hidden sheets; when they are included, the `sheets.open` trace section marks them with `hidden`.

## Tracing and coverage

- `sheets.open` (source `ProtoTest.Sheets`) carries `sheets.name` and a Fields section listing every sheet read as `name · {rows}x{columns}[ hidden]`.
- `sheets.model` carries `sheets.sheet` and `sheets.columns` when a typed model is read.
- Every assertion is an `assert.sheets` operation with attributes such as `sheets.cell`, `sheets.header`, `sheets.range`, `sheets.column`, `sheets.expected` and `sheets.actual`. The operation opens before the check, so a failure still leaves evidence with the fixed polarity.
- Reading a cell or range records a `sheets.range` observation (`"{Sheet}!{reference}"`), and opening a workbook records `sheets.workbook` with a `sheets.count` metadata value. `SheetsCoverageCollector` (category `Sheets`) aggregates the range reads.

Reads are recorded by cell and range reads, `Table.Column`, `RowWhere`, `Rows`, the `ProtoTableRow` indexer, and model `Rows`, `Column` and `Verify`. Building a table view records nothing, and a malformed reference throws before any read is recorded. Coverage therefore means **"verified"**, not "present in the file":

```csharp
var coverage = Proto.Context.Services.GetServices<IProtoCollector>()
    .OfType<SheetsCoverageCollector>().Single()
    .GetReportItems();
```

Parse a workbook without a test context and there is no coverage — reads silently record nothing.

## Skip

The capability is name `"Sheets"`, kind `document` (`ProtoCapabilityKinds.Document`). Skip with:

```csharp
[RequiresCapability(ProtoCapabilityKinds.Document)]
```

`[Sheet]` and `[Column]` are modeling attributes, not skip conditions. See [Skip conditions](../../foundation/skip-conditions.md).

## Limits

- **OpenXML `.xlsx` only.** No writing, no `.xls`, no CSV, and no producer library is involved on either side.
- **One million cells per range.** Reading a bigger area, or a reversed rectangle, throws; merge propagation is skipped above 1,000,000 cells in a merge.
- **Dates are a heuristic.** A numeric cell counts as a date when its style or number format says so; the rule is not a schema.
- **Cached formula values only.** ProtoTest never recalculates; the formula text and the cached result are what the file holds.
- **`ShouldAll` has no negated counterpart**, and typed columns read the whole declared range whether or not the test looks at every value.
- **Coverage is read-based.** A column present in the file but never read is uncovered; hidden sheets are excluded by default.
- **Header paths are ordinal.** Matching is case-sensitive, and a suffix match is only allowed when exactly one column matches.

## Links

- [Integrations overview](../overview.md) — where the document package sits.
- [Shape matching](../../foundation/shape-matching.md) — the rules behind `ShouldMatchShape` on model rows.
- [Coverage](../../observability/coverage.md) — how collectors and report items work.
- The demo's report journey: [`samples/ProtoTest.Demo/SheetsJourney.cs`](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/SheetsJourney.cs).
