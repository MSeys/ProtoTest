---
id: artifacts-and-reports
title: Archive and reports
sidebar_label: Archive and reports
sidebar_position: 3
description: "What a .prototrace holds, where the reports live inside it, and how to open one."
---

import LearnShell from '@site/src/components/LearnShell';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import Link from '@docusaurus/Link';

# Archive and reports

One file from CI must explain the run to a reader who was not there. That file is the `.prototrace`.

<LearnShell
  level="Level 4, lesson 3"
  minutes="About 6 minutes"
  outcome={[
    'Name what a .prototrace holds.',
    'Find the JSON and HTML reports inside an archive.',
    'Open an archive in the viewer and in an archive tool.',
  ]}
  before={[
    <>Contract coverage (<Link to="/learn/evidence/contract-coverage">lesson 2</Link>).</>,
    'Nothing installed. The archives are on this site.',
  ]}
  situation={
    <>
      <p>A CI job uploads one file. The reviewer opens it and sees the run.</p>
      <p>The sample configures both report sinks in one place, and the archive carries the reports next to the trace. This lesson opens that file.</p>
    </>
  }
  checkpoint={{
    question:
      'A CI job uploads one archive. Where does the reviewer walk the operations, and where do they read the JSON report?',
    verify: (
      <>
        Download <a href="pathname:///lessons/l4-artifacts.prototrace">l4-artifacts.prototrace</a> and open it twice: once in the viewer, once with an archive tool (the file is a zip).
      </>
    ),
    reveal: (
      <>
        The viewer walks the operations and the state. The JSON report sits inside the same file at <code>resources/run/JsonReportSink/run-artifact-1/report.json</code>. One upload carries both.
      </>
    ),
  }}
  learned={[
    'A .prototrace holds the operations, the state, the source files, the attachments and the reports.',
    'Both reports are written at the end of the run and copied into the archive.',
    'The viewer draws the execution story; the reports are files inside the same archive.',
  ]}
  next={[
    {
      label: 'Workbook as attachment',
      to: '/learn/evidence/workbook-as-attachment',
      note: 'One journey keeps a downloaded workbook as evidence, and checks it through a record model.',
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
    trace.ActivitySources.Add("Northstar.Domain");
})
.AddSink<JsonReportSink>(sink => sink.OutputPath = Path.Combine(
    "TestResults", "Northstar.ProtoTest", "report.json"))
.AddSink<HtmlReportSink>(sink =>
{
    sink.OutputPath = Path.Combine("TestResults", "Northstar.ProtoTest", "report.html");
    sink.Title = "Northstar Learning demo";
});`}
  callouts={[
    {line: 3, title: 'The application spans', note: 'The domain activity source is captured into the trace, one archive per run, so a rerun never overwrites the last run.'},
    {line: 5, title: 'Both sinks copied into the archive', note: 'The JSON and HTML reports are written beside the trace and copied into resources/, so one upload carries all three.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/Setup.cs</code>. The sink paths below <code>TestResults/Northstar.ProtoTest/</code> are the local default.</>}
/>

Both files are written at the end of the run and copied into the archive.

One artifact carries the story and the report.

## Reference

Local defaults for one run:

| Output | Default path |
| --- | --- |
| Trace archive | `TestResults/prototest-{runId}.prototrace`, one archive per run |
| JSON report | `TestResults/Northstar.ProtoTest/report.json` |
| HTML report | `TestResults/Northstar.ProtoTest/report.html` |
| In-archive JSON report | `resources/run/JsonReportSink/run-artifact-1/report.json` |

## How to read one

1. Download the archive from a lesson, a report link or a CI artifact.
2. Open it in the [viewer](https://trace.prototest.dev) to walk the operations and the state.
3. Open the same file with an archive tool to read `resources/run/JsonReportSink/run-artifact-1/report.json`, or open the `report.html` beside it in a browser.
4. Open an attachment under <code>{'resources/<test id>/'}</code> to see the exact bytes the test checked.

The viewer reads the file locally in the browser. Nothing is uploaded anywhere.

</LearnShell>
