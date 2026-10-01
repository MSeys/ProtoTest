---
id: workbook-as-attachment
title: Workbook as attachment
sidebar_label: Workbook as attachment
sidebar_position: 4
description: "How one journey keeps a downloaded workbook as evidence and reads it through a record model."
---

import LearnShell from '@site/src/components/LearnShell';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import Link from '@docusaurus/Link';

# Workbook as attachment

Some evidence does not fit in a span. This lesson follows one download into the archive: a real workbook, kept with its bytes, checked through a record model.

<LearnShell
  level="Level 4, lesson 4"
  minutes="About 6 minutes"
  outcome={[
    'Say where a downloaded file lands after the run.',
    'Read a workbook through a record model.',
    'Name the checks the model recorded.',
  ]}
  before={[
    <>Archive and reports (<Link to="/learn/evidence/artifacts-and-reports">lesson 3</Link>).</>,
    'Nothing installed. The archives are on this site.',
  ]}
  situation={
    <>
      <p>A downloaded workbook, a response body, a summary a hook attached: the run keeps those as attachments, with the bytes.</p>
      <p>A reader two days later holds the same file the test checked.</p>
      <p>The sheets journey is built to show that. The application writes a real OpenXML workbook, the test downloads it and asserts it through a record model, and the workbook lands in the archive.</p>
    </>
  }
  checkpoint={{
    question:
      'The test never writes the workbook to disk. Where is it after the run, and what shows that the sheet model was checked?',
    verify: (
      <>
        Download <a href="pathname:///lessons/l4-artifacts.prototrace">l4-artifacts.prototrace</a>, open it with an archive tool (the file is a zip), and look under <code>resources/</code>.
      </>
    ),
    reveal: (
      <>
        The response bytes are an attachment inside the archive, under <code>resources/&lt;test id&gt;/artifact-1/&lt;test id&gt;-rest-01-response</code>, 2,010 bytes of workbook.
        The execution layer holds the <code>sheets.open</code>, <code>sheets.model</code> and <code>assert.sheets</code> entries.
        The embedded report carries the sheet coverage rows the model and the column check recorded, <code>Summary!A2:C2</code> and <code>Summary!C2:C2</code>.
      </>
    ),
  }}
  learned={[
    'Response bytes live in the archive as attachments, so the evidence travels with the run.',
    'A record model turns layout expectations into ordinary checks.',
    'The model and the column check each leave a coverage row in the report.',
  ]}
  next={[
    {
      label: 'Findings',
      to: '/learn/evidence/findings',
      note: 'A teardown failure becomes a finding, and the report keeps it beside the test result.',
    },
    {
      label: 'Reporting',
      to: '/docs/observability/reporting',
      note: 'The JSON and HTML sinks, and what each report section holds.',
    },
  ]}>

## The journey that proves it

`SheetsJourney.TheMonthlyReportMatchesItsModel` creates a project, downloads the monthly report and reads it as records:

<AnnotatedCode
  filename="SheetsJourney.cs"
  code={`[Sheet("Summary", HeaderRows = [1])]
public sealed record ProjectReportRow(
    [property: Column("Name", Unique = true)] string Name,
    [property: Column("Status", Pattern = "^[a-z]+$")] string Status,
    [property: Column("Environments", Min = 0)] int Environments);`}
  callouts={[
    {line: 1, title: 'Describe the sheet once', note: 'The record names the sheet, the header row and the columns, and the model check reads the workbook against it.'},
    {line: 3, title: 'Rules per column', note: 'Unique, a pattern, a minimum: the model turns layout expectations into ordinary checks.'},
  ]}
  foot={<>The test body opens the response with <code>Proto.Context.Sheets()</code>. It calls <code>report.Should.MatchModel()</code>. It checks a column. It reads the row it created.</>}
/>

From the archive:

| Entry | Reading |
| --- | --- |
| `http.request` REST `GET /api/v1/reports/monthly.xlsx`, 128.5 ms, HTTP 200 | the download, with the response attached |
| `sheets.open`, 28.2 ms, then `sheets.model` | the workbook was opened and the record model built |
| `assert.sheets`, `Summary.Environments` | the column check the model recorded |
| `attachment.publish`, `<test id>-rest-01-response` | the workbook's bytes, kept in the run |

## Reference

How the record maps to the sheet:

| Model piece | What it names |
| --- | --- |
| `Sheet("Summary", HeaderRows = [1])` | the sheet and its header row |
| `Column("Name", Unique = true)` | the column, with one value per row |
| `Column("Status", Pattern = "^[a-z]+$")` | the column, with a value rule |
| `Column("Environments", Min = 0)` | the column, with a floor |

</LearnShell>
