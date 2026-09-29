---
sidebar_position: 5
title: A downloaded report matches its model
description: "Download the workbook the application generates over REST and verify it with a typed sheet model: layout, column rules and the row the test created."
---

import TraceExample from '@site/src/components/TraceExample';

# A downloaded report matches its model

## The situation

The application generates a monthly report as an `.xlsx`. A `200` on the download says a file arrived. It does not say the file has the right sheet, the right columns, valid values in every row or the project the test created.

The test downloads the workbook over the API and checks it the way a reader would. The demo runs this journey in [SheetsJourney.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/Northstar.ProtoTest/SheetsJourney.cs).

## The code

### Compose

The workbook is read as OpenXML, so it makes no difference whether the application wrote it with ClosedXML, EPPlus or anything else:

```csharp
// Setup.cs: one line adds the document capability.
builder.AddSheets();
```

Describe a row of the report once, with the rules every value must meet:

```csharp
[Sheet("Summary", HeaderRows = [1])]
public sealed record ProjectReportRow(
    [property: Column("Name", Unique = true)] string Name,
    [property: Column("Status", Pattern = "^[a-z]+$")] string Status,
    [property: Column("Environments", Min = 0)] int Environments);
```

### The test

The test creates a project the report must contain, downloads the report and reads it through the model:

```csharp
[Application(NorthstarTargets.Api)]
[NorthstarMember(PlanIds.Growth)]
public sealed class SheetsJourney
{
    [ProtoTest]
    [SignedInAs]
    public async Task TheMonthlyReport_ShouldMatchItsModel()
    {
        // Arrange: a project the report must contain.
        var project = await Proto.Context.Data()
            .For<CreateProjectRequest>()
            .With(request => request.Name, "report-atlas")
            .CreateAsync<ProjectResponse>();

        // Act: download the generated workbook; the response is content the sheet reader understands.
        using var response = await Proto.Context.Rest().GetAsync("/api/v1/reports/monthly.xlsx");
        response.Should.HaveHttpStatus(HttpStatusCode.OK);
        var report = Proto.Context.Sheets().Open(response).Model<ProjectReportRow>();

        // Assert: the model checks the layout, and the exact values are ordinary assertions.
        report.Should.MatchModel();
        report.Column(row => row.Environments).Should.All(count => count >= 0);
        var row = report.Row(candidate => candidate.Name == "report-atlas");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.Status, Is.EqualTo(project.Status));
            Assert.That(row.Environments, Is.EqualTo(0));
        }
    }
}
```

`Open(response)` needs no file: a REST response is named content, so the workbook is read straight from it. `Should.MatchModel()` checks the model's rules across every row, and `Row(...)` proves the report contains the project this test created.

## What the trace shows

The operations land in the order the test caused them:

- the project creation's own requests,
- the report download as an `http.request` with its `http.response` observation and, when [capture](../integrations/rest/attachments.md) is on, the workbook itself as a response artifact,
- `sheets.open` with the sheet it read, then `sheets.model` with the declared columns, and one `assert.sheets` operation per assertion.

Reading a cell or a range records a `sheets.range` observation, and `SheetsCoverageCollector` aggregates those. Opening a workbook is evidence, not coverage: a range is covered only when a read verified it. See [Sheets](../integrations/sheets/index.md#in-the-trace-and-coverage).

This is the demo's own run:

<TraceExample
  demo="workbook"
  title="The monthly report matches its model"
  path="Data → REST download → workbook model → checks"
/>

## Variations

- **Check the layout separately.** `report.Should.MatchHeaders()` compares the sheet's header row with the model's columns in declaration order, so a moved column fails without reading values.
- **A label/value sheet.** `[Sheet("Summary", Kind = ProtoSheetKind.KeyValue)]` with `[Label("Total")]` properties reads a two-column summary, and `summary.Column(s => s.Total).Should.Be(123.45m)` reads one value. See [Key-value sheets](../integrations/sheets/index.md#key-value-sheets).
- **No model.** `Sheet("Summary").Cell("B4").Should.Be(1200m)` and `Range("A4:B5").Should.Match(...)` work on a workbook without a record, for a one-off check.
- **A file or a stream.** `Open(path)` and `Open(stream)` take the same workbook from disk or memory.

## What it does not prove

- **OpenXML `.xlsx` only.** There is no `.xls`, no CSV and no writing.
- **`Should.MatchModel()` reports everything at once.** A missing header fails when the model is read, naming it; broken column rules are collected, each with its cell reference, into one failure.
- **Formulas are cached values.** Nothing is recalculated, and dates are detected from the cell's style, not a schema.
- **Ranges are capped at 1,000,000 cells**, and hidden sheets are skipped unless the options ask for them.
- **Coverage is read-based.** A column present in the file but never read is uncovered.
