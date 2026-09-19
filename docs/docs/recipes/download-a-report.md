---
sidebar_position: 5
title: A downloaded report matches its model
description: Download the workbook the application generates over REST and verify it with a typed sheet model — layout, column rules and the row the test created.
---

# A downloaded report matches its model

The application generates a monthly report as an `.xlsx`. The test downloads it over the API and checks it the way a reader would: the right sheet, the right columns, every value within its rules, and the row for the project the test created.

## Compose

```csharp
using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.Rest;
using ProtoTest.Sheets;

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
        created.ShouldHaveHttpStatus(HttpStatusCode.Created);

        using var response = await Proto.Context.Rest().GetAsync("/api/reports/monthly.xlsx");
        response.ShouldHaveHttpStatus(HttpStatusCode.OK);

        var report = Proto.Context.Sheets().Open(response).Model<ProjectReportRow>();

        report.Verify();
        report.Row(row => row.Name == name)
            .ShouldMatchShape(new { Status = "active", Environments = 0 });
    }
}
```

## Watch for

- **`Open(response)` needs no file.** A REST response is named content, so the workbook is read straight from it. Turn on [REST capture](../integrations/rest/attachments.md) to keep the exact file with the test.
- **`Verify()` reports everything at once.** A missing header fails as soon as the model is read, naming it; broken column rules are collected, each with its cell reference, into one failure.
- **The library does not matter.** The workbook is read as OpenXML, so it makes no difference whether the application wrote it with ClosedXML, EPPlus or anything else.

See [Sheets](../integrations/sheets/index.md) for single cells, formulas and ranges.
