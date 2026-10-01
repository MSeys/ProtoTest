---
id: artifacts-and-reports
title: What is inside the file CI uploads?
sidebar_label: Archive and reports
sidebar_position: 8
description: "Open a .prototrace in the viewer and with an archive tool, and find the operations and the JSON and HTML reports inside it."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import Link from '@docusaurus/Link';

# What is inside the file CI uploads?

<Lesson
  track="Understand failures"
  step="Lesson 8 of 9"
  minutes={6}
  outcomes={[
    'Name what a .prototrace holds',
    'Find the JSON and HTML reports inside an archive',
    'Open an archive in the viewer and in an archive tool',
  ]}
  needs={[
    <>The previous lesson, <Link to="/learn/understand-failures/contract-coverage">contract coverage</Link>, which already opened one report</>,
    'Nothing installed. The archives are on this site.',
  ]}
/>

## The problem

A CI job uploads one file, and a reviewer who was not there has to understand the run from it. That file is the `.prototrace`. You need to know what is in it and how to open each part.

## Do it

### 1. Download an archive

Download [l4-artifacts.prototrace](pathname:///lessons/l4-artifacts.prototrace). It is a zip file.

### 2. Open it in the viewer

Drop it on the [viewer](https://trace.prototest.dev). You see the operations, each recorded step, and the state: what existed during the run and how it changed. The viewer reads the file locally in your browser, so nothing is uploaded.

### 3. Open it with an archive tool

Rename the copy to `.zip` if your tool needs it. Look for these entries:

| Entry | What it is |
| --- | --- |
| `spans.json` | the operations: setup, execution, teardown, every request, check and release |
| `state.json` | what existed and changed: clients, contexts, resources and tracked values |
| `sources/` | the source files the recording touched, so the trace can name a line |
| `resources/` | the attachments and the run artifacts, including the reports |
| `manifest.json` | the map of the entries above |

The JSON report is at `resources/run/JsonReportSink/run-artifact-1/report.json`. An attachment sits under `resources/<test id>/` and holds the exact bytes the test checked.

### 4. Find the reports locally

A local run also writes the files next to the archive:

| Output | Default path |
| --- | --- |
| Trace archive | `TestResults/prototest-{runId}.prototrace`, one archive per run |
| JSON report | `TestResults/Northstar.ProtoTest/report.json` |
| HTML report | `TestResults/Northstar.ProtoTest/report.html` |

Open `report.html` in a browser for the readable version of the report.

## What happened

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
    {line: 3, title: 'The application\'s own operations', note: 'The domain activity source is captured into the trace, one archive per run, so a rerun never overwrites the last run.'},
    {line: 5, title: 'Both sinks copied into the archive', note: 'The JSON and HTML reports are written beside the trace and copied into resources/, so one upload carries all three.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/Setup.cs</code>. The sink paths below <code>TestResults/Northstar.ProtoTest/</code> are the local default.</>}
/>

A sink writes a report at the end of the run. Both files are also copied into the archive, so one artifact carries the story and the report. The viewer draws the story. The reports are plain files in the same zip.

## Check yourself

<Checkpoint
  question="A CI job uploads one archive. Where does the reviewer walk the operations, and where do they read the JSON report?"
  verify={<>Open <a href="pathname:///lessons/l4-artifacts.prototrace">l4-artifacts.prototrace</a> twice: once in the viewer, once with an archive tool.</>}>

The viewer walks the operations and the state. The JSON report sits inside the same file at `resources/run/JsonReportSink/run-artifact-1/report.json`. One upload carries both.

</Checkpoint>

## Remember

- A `.prototrace` holds the operations, the state, the source files, the attachments and the reports.
- Both reports are written at the end of the run and copied into the archive.
- The viewer draws the execution story. The reports are files inside the same archive.

## Go deeper

- [Reporting](/docs/observability/reporting): the JSON and HTML sinks, and what each report section holds.
- [Workbook as attachment](/learn/across-boundaries/workbook-as-attachment): one journey keeps a downloaded workbook as evidence.
