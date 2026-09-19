---
sidebar_position: 5
title: A downloaded report matches its model
description: Download the workbook the application generates over REST and verify it with a typed sheet model — layout, column rules and the row the test created.
---

# A downloaded report matches its model

The application generates a monthly report as an `.xlsx`. The test downloads it over the API and checks it the way a reader would: the right sheet, the right columns, every value within its rules, and the row for the project the test created.

The same journey runs in the demo — [SheetsJourney.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/SheetsJourney.cs) (test); its host calls `.AddSheets()` in [Setup.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/Setup.cs). The full API surface is in [Sheets](../integrations/sheets/index.md).

## Compose

```csharp
protected override void Configure(IProtoHostBuilder builder) =>
    builder
        .AddApplication("Api", app => app
            .AddAspNetCoreServer<Program>()
            .AddRest(rest => rest.AddClient("Api")))
        .AddSheets();
```

## The model

Describe a row of the report once, with the rules every value must meet:

```csharp
[Sheet("Summary", HeaderRows = [1])]
public sealed record ProjectReportRow(
    [property: Column("Name", Unique = true)] string Name,
    [property: Column("Status", Pattern = "^[a-z]+$")] string Status,
    [property: Column("Environments", Min = 0)] int Environments);
```

## The test

```csharp
[Application("Api")]
public sealed class MonthlyReportTests
{
    [ProtoTest]
    public async Task The_monthly_report_lists_a_new_project()
    {
        var name = $"report-{Proto.Context.TestId}";
        using var created = await Proto.Context.Rest()
            .Body(new { name })
            .PostAsync("/api/projects");
        created.Should.HaveHttpStatus(HttpStatusCode.Created);

        using var response = await Proto.Context.Rest().GetAsync("/api/reports/monthly.xlsx");
        response.Should.HaveHttpStatus(HttpStatusCode.OK);

        var report = Proto.Context.Sheets().Open(response).Model<ProjectReportRow>();

        report.Verify();
        report.Row(row => row.Name == name)
            .ShouldMatchShape(new { Status = "active", Environments = 0 });
    }
}
```

## What it proves

`Verify()` checks the model's rules across every row — uniqueness, patterns, minimums — and `Row(...)` proves the report actually contains the project this test created. The workbook is read as OpenXML, so it makes no difference whether the application wrote it with ClosedXML, EPPlus or anything else.

## Limits

- **`Open(response)` needs no file.** A REST response is named content, so the workbook is read straight from it. Turn on [REST capture](../integrations/rest/attachments.md) to keep the exact file with the test.
- **`Verify()` reports everything at once.** A missing header fails when the model is read, naming it; broken column rules are collected, each with its cell reference, into one failure.
- **OpenXML `.xlsx` only.** There is no `.xls`, no CSV and no writing.
- **Formulas are cached values.** Nothing is recalculated, and dates are detected from the cell's style, not a schema.
- **Ranges are capped at 1,000,000 cells**, and hidden sheets are skipped unless the options ask for them.
