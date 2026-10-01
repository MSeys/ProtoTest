---
id: workbook-as-attachment
title: Check a generated file
sidebar_label: Check a generated file
sidebar_position: 6
description: "Download a workbook the application generated, check it through a record model and find it in the trace."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';
import Link from '@docusaurus/Link';

# Check a generated file

<Lesson
  track="Across boundaries"
  step="Lesson 6 of 6"
  minutes={7}
  outcomes={[
    'Describe the layout of a downloaded workbook as a record',
    'Check the declared columns and selected values in one test',
    'Find the workbook in the trace archive',
  ]}
  needs={[
    <>The previous lesson, <Link to="/learn/across-boundaries/follow-a-message">Follow a message through a broker</Link></>,
    'The Northstar sample checkout and .NET 8 SDK, with default local settings',
  ]}
/>

## The problem

Applications generate files: reports, exports, invoices. A successful download does not prove that a report contains the expected values. A record model lets the test name columns instead of repeating cell coordinates.

## Do it

### 1. Run the test alone

From the repository root, run the existing `SheetsJourney` test:

```bash
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~TheMonthlyReportMatchesItsModel"
```

With default settings, it reports one passed test. It uses the local application and SQLite store, so no broker or browser is needed.

The snippets below come from `samples/Northstar.ProtoTest/SheetsJourney.cs`. Its class selects the API application and prepares a Growth-plan tenant. `[SignedInAs]` declares the identity. The sample authenticator supplies the tenant member's credentials for the download.

### 2. Describe the sheet as a record

```csharp
[Sheet("Summary", HeaderRows = [1])]
public sealed record ProjectReportRow(
    [property: Column("Name", Unique = true)] string Name,
    [property: Column("Status", Pattern = "^[a-z]+$")] string Status,
    [property: Column("Environments", Min = 0)] int Environments);
```

`[Sheet]` and `[Column]` are attributes from ProtoTest's Sheets integration. The record names the `Summary` sheet and header row 1. Each property maps to a named column. The rules require distinct names, lowercase status text and nonnegative environment counts.

Binding the model requires those headers to exist. The model reads typed values, so a changed cell position need not change the test when the header still matches.

### 3. Download and open the workbook

First the test creates a project the report must contain. `Data().For<T>()` starts a request with the sample's defaults, `With` changes one field, and `CreateAsync` creates it. Then the test downloads the report:

```csharp
var project = await Proto.Context.Data()
    .For<CreateProjectRequest>()
    .With(request => request.Name, "report-atlas")
    .CreateAsync<ProjectResponse>();

using var response = await Proto.Context.Rest().GetAsync("/api/v1/reports/monthly.xlsx");
response.Should.HaveHttpStatus(HttpStatusCode.OK);
var report = Proto.Context.Sheets().Open(response).Model<ProjectReportRow>();
```

The download is an ordinary REST call. `Sheets().Open(response)` reads its bytes as an OpenXML workbook. `Model<ProjectReportRow>()` binds the record to its sheet and columns. It does not yet run every declared value rule.

### 4. Check the layout, then the values

```csharp
report.Should.MatchModel();
report.Column(row => row.Environments).Should.All(count => count >= 0);
var row = report.Row(candidate => candidate.Name == "report-atlas");
using (Assert.EnterMultipleScope())
{
    Assert.That(row.Status, Is.EqualTo(project.Status));
    Assert.That(row.Environments, Is.EqualTo(0));
}
```

`MatchModel()` checks the cells in each declared column against the model's rules. It does not require the exact header order or reject extra columns. Use `MatchHeaders()` when those are requirements.

The column's `Should.All` records a check that every environment count is nonnegative. `Row(...)` returns the first matching typed row and throws if none matches. The NUnit scope checks that row's status and its zero environment count together.

This test does not check the report's total row count. It checks the declared rules and the selected project's values.

### 5. Find the file in the archive

Download [l4-artifacts.prototrace](pathname:///lessons/l4-artifacts.prototrace) and open it with an archive tool. A `.prototrace` file is a ZIP archive.

Under `resources/`, find the workbook attachment at `resources/<test id>/artifact-1/<test id>-rest-01-response`. Its attachment metadata identifies the spreadsheet media type. The entry has no `.xlsx` extension.

In the [viewer](https://trace.prototest.dev), this test's execution phase shows `Sheets · open monthly.xlsx`, `Sheets · model ProjectReportRow` and `Sheets · Summary.Environments`.

Your new run writes its trace under `samples/Northstar.ProtoTest/bin/Debug/net8.0/TestResults/`. Test ids and attachment paths can differ from the saved example.

## What happened

The setup class tells the REST client to keep request and response bodies as attachments, with `CaptureAttachments()`. For this binary response, REST saves the downloaded bytes. Opening them with Sheets does not itself attach a workbook.

The saved archive therefore carries the file the test read. Its embedded report lists the ranges read as coverage, `Summary!A2:C2` and `Summary!C2:C2`. Those ranges describe this one-row example. They do not prove that every workbook requirement was asserted.

The model and column checks have their own trace operations. The plain NUnit checks do not each become a ProtoTest check operation. A failure still appears in the test's outcome.

## Check yourself

<Checkpoint question="The test never writes the workbook to disk. Where is it after the run?">

Inside the trace archive, as an attachment of this binary REST response. The setup class enables that capture with `CaptureAttachments()` on the REST integration. Sheets reads the bytes. Opening a workbook alone does not save it as an attachment.

</Checkpoint>

## Remember

- Describe the file as a record, once.
- Use `MatchModel()` for declared column rules and `MatchHeaders()` for exact header shape.
- With REST attachment capture enabled, this downloaded workbook travels with the trace.

## Go deeper

- [Sheets integration](/docs/integrations/sheets): cells, ranges and typed rows.
- [Attachments](/docs/integrations/rest/attachments): what REST keeps in the trace.
- Next track: [Understand failures](/learn/understand-failures/findings), where a failing teardown becomes a finding.
