---
id: artifacts-and-reports
title: The archive and the reports
sidebar_label: The archive and the reports
sidebar_position: 3
description: "What a .prototrace holds, where the reports live inside it, and how one journey keeps a downloaded workbook as evidence."
---

import LearnShell from '@site/src/components/LearnShell';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import Link from '@docusaurus/Link';

# The archive and the reports

One file from CI must explain the run to a reader who was not there. That file is the `.prototrace`: it holds the execution story, the state, the attachments and the reports the run wrote.

<LearnShell
  level="Level 4, lesson 3"
  minutes="About 8 minutes"
  outcome={[
    'Name what a .prototrace holds.',
    'Find the JSON and HTML reports inside an archive.',
    'Read a downloaded workbook and the checks it went through.',
  ]}
  before={[
    <>Contract coverage, not code coverage (<Link to="/learn/evidence/contract-coverage">lesson 2</Link>).</>,
    'Nothing installed. The archives are on this site.',
  ]}
  situation={
    <>
      <p>Some evidence does not fit in a span. A downloaded workbook, a response body, a summary a hook attached: the run keeps those as attachments, with the bytes, so a reader two days later holds the same file the test checked.</p>
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
        The response bytes are an attachment inside the archive, under <code>resources/&lt;test id&gt;/artifact-1/&lt;test id&gt;-rest-01-response</code>, 2,009 bytes of workbook. The execution layer holds the <code>sheets.open</code>, <code>sheets.model</code> and <code>assert.sheets</code> entries, and the embedded report carries the sheet coverage rows the model and the column check recorded, <code>Summary!A2:C2</code> and <code>Summary!C2:C2</code>.
      </>
    ),
  }}
  learned={[
    'A .prototrace holds the operations, the state, the source files, the attachments and the reports.',
    'Response bytes live in the archive as attachments, so the evidence travels with the run.',
    'The viewer draws the execution story; the reports are files inside the same archive.',
  ]}
  next={[
    {
      label: 'Read the findings and the run gate',
      to: '/learn/evidence/read-the-findings-and-the-run-gate',
      note: 'A teardown failure becomes a finding, and a gate turns it into a failed run.',
    },
    {
      label: 'Reporting',
      to: '/docs/observability/reporting',
      note: 'The JSON and HTML sinks, and what each report section holds.',
    },
  ]}>

## What the archive holds

A `.prototrace` is a zip with named entries:

| Entry | What it is |
| --- | --- |
| `spans.json` | the operations: setup, execution, teardown, every request, check and release |
| `state.json` | what existed and changed: clients, contexts, resources and tracked values |
| `sources/` | the source files the recording touched, so the trace can name a line |
| `resources/` | the attachments and the run artifacts, including the reports |
| `manifest.json` | the map of the entries above |

The sample configures both report sinks in one place:

<AnnotatedCode
  filename="Setup.cs"
  code={`.ConfigureTracing(trace =>
{
    trace.OutputPath = Path.Combine("TestResults", "Northstar.ProtoTest", "northstar.prototrace");
})
.AddSink<JsonReportSink>(sink => sink.OutputPath = Path.Combine(
    "TestResults", "Northstar.ProtoTest", "report.json"))
.AddSink<HtmlReportSink>(sink =>
{
    sink.OutputPath = Path.Combine("TestResults", "Northstar.ProtoTest", "report.html");
    sink.Title = "Northstar Learning demo";
});`}
  callouts={[
    {line: 3, title: 'The trace path', note: 'One archive per run. CI points this at its artifact directory instead; the evidence lesson shows how.'},
    {line: 5, title: 'Both sinks copied into the archive', note: 'The JSON and HTML reports are written beside the trace and copied into resources/, so one upload carries all three.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/Setup.cs</code>. The paths below <code>TestResults/Northstar.ProtoTest/</code> are the local default.</>}
/>

Both files are written at the end of the run and copied into the archive, so the one artifact a CI job uploads carries the story and the report.

## The journey that proves it

`SheetsJourney.TheMonthlyReport_ShouldMatchItsModel` creates a project, downloads the monthly report and reads it as records:

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
  foot={<>The test body opens the response with <code>Proto.Context.Sheets()</code>, calls <code>report.Should.MatchModel()</code>, checks a column and reads the row it created.</>}
/>

From the archive:

| Entry | Reading |
| --- | --- |
| `http.request` REST `GET /api/v1/reports/monthly.xlsx`, 128.5 ms, HTTP 200 | the download, with the response attached |
| `sheets.open`, 28.2 ms, then `sheets.model` | the workbook was opened and the record model built |
| `assert.sheets`, `Summary.Environments` | the column check the model recorded |
| `attachment.publish`, `<test id>-rest-01-response` | the workbook's bytes, kept in the run |

## How to read one

1. Download the archive from a lesson, a report link or a CI artifact.
2. Open it in the [viewer](https://trace.prototest.dev) to walk the operations and the state.
3. Open the same file with an archive tool to read `resources/run/JsonReportSink/run-artifact-1/report.json`, or open the `report.html` beside it in a browser.
4. Open an attachment under <code>{'resources/<test id>/'}</code> to see the exact bytes the test checked.

The viewer reads the file locally in the browser. Nothing is uploaded anywhere.

</LearnShell>