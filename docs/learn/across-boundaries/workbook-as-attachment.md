---
id: workbook-as-attachment
title: Check a generated file
sidebar_label: Check a generated file
sidebar_position: 6
description: "Download a workbook the application generated, check it through a record model and find it in the trace."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';

# Check a generated file

<Lesson
  track="Across boundaries"
  step="Lesson 6 of 6"
  minutes={7}
  outcomes={[
    'Describe the layout of a downloaded workbook as a record',
    'Check the layout and the values in one test',
    'Find the workbook in the trace archive',
  ]}
  needs={[
    <>The previous lesson, <a href="./follow-a-message">Follow a message through a broker</a></>,
  ]}
/>

## The problem

Applications generate files: reports, exports, invoices. A test that only checks the download status proves nothing about the file. A test that opens it with a spreadsheet library ties itself to cell coordinates that change with every layout edit.

## Do it

### 1. Run the test alone

```bash
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~TheMonthlyReportMatchesItsModel"
```

### 2. Describe the sheet as a record

```csharp
[Sheet("Summary", HeaderRows = [1])]
public sealed record ProjectReportRow(
    [property: Column("Name", Unique = true)] string Name,
    [property: Column("Status", Pattern = "^[a-z]+$")] string Status,
    [property: Column("Environments", Min = 0)] int Environments);
```

The record names the sheet and its header row. Each property maps to a column, with a rule: unique names, a status that matches a pattern, a count that is at least 0.

### 3. Download and open the workbook

First the test creates a project the report must contain, then it downloads the report:

```csharp
var project = await Proto.Context.Data()
    .For<CreateProjectRequest>()
    .With(request => request.Name, "report-atlas")
    .CreateAsync<ProjectResponse>();

using var response = await Proto.Context.Rest().GetAsync("/api/v1/reports/monthly.xlsx");
response.Should.HaveHttpStatus(HttpStatusCode.OK);
var report = Proto.Context.Sheets().Open(response).Model<ProjectReportRow>();
```

The download is an ordinary REST call. `Sheets().Open(response)` reads the response bytes as a workbook, and `Model<ProjectReportRow>()` applies the record to it.

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

`MatchModel()` checks the whole sheet against the record's rules. The column check and the row lookup then use ordinary assertions on typed values.

### 5. Find the file in the archive

Download [l4-artifacts.prototrace](pathname:///lessons/l4-artifacts.prototrace) and open it with an archive tool, because it is a zip file. Under `resources/` you find the response bytes as an attachment, in a path like `resources/<test id>/artifact-1/<test id>-rest-01-response`. In the [viewer](https://trace.prototest.dev), the execution phase of this test shows `Sheets · open monthly.xlsx`, `Sheets · model ProjectReportRow` and `Sheets · Summary.Environments`.

## What happened

The sample's setup class asks the REST integration to capture attachments, so the downloaded bytes were saved in the run's trace. A reader two days later holds the same file the test checked.

The record turned layout expectations into checks. The embedded report lists the cells the model and the column check covered, `Summary!A2:C2` and `Summary!C2:C2`.

## Check yourself

<Checkpoint question="The test never writes the workbook to disk. Where is it after the run?">

Inside the trace archive, as an attachment of the test's REST response. The setup class enables it with `CaptureAttachments()` on the REST integration.

</Checkpoint>

## Remember

- Describe the file as a record, once.
- `MatchModel()` checks layout and rules. Ordinary assertions check values.
- The downloaded bytes travel with the trace.

## Go deeper

- [Sheets integration](/docs/integrations/sheets): cells, ranges and typed rows.
- [Attachments](/docs/integrations/rest/attachments): what REST keeps in the trace.
- Next track: [Understand failures](/learn/understand-failures/findings), where a failing teardown becomes a finding.
