---
id: install-and-run
title: Run the sample suite
sidebar_label: Install and run
sidebar_position: 1
description: "Run the Northstar.ProtoTest sample suite with one command, find the trace it leaves, and read why some tests skip."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';

# Run the sample suite

<Lesson
  track="Start"
  step="Lesson 1 of 3"
  minutes={8}
  outcomes={[
    'Run the sample suite and get a green result.',
    'Find the trace and the reports the run leaves behind.',
    'Say why a test skipped.',
  ]}
  needs={[
    'Git and the .NET 10.0.401 SDK selected by the repository.',
    'The .NET 8 and ASP.NET Core 8 runtimes. Installing the .NET 8 SDK provides both.',
    'An internet connection for the first package restore.',
  ]}
/>

## The problem

Before you trust a test framework, you want to see it work. This lesson runs the Northstar sample suite and finds the files it produces. The suite tests a small application through an API, a database, a browser and a message broker.

The default configuration runs locally. Tests that need an unavailable broker or browser skip with a reason.

## Do it

### 1. Run the suite

```bash
git clone https://github.com/MSeys/ProtoTest.git
cd ProtoTest
dotnet test samples/Northstar.ProtoTest
```

The repository's `global.json` selects the build SDK. The sample targets `net8.0`, so it also needs the .NET 8 runtimes listed above.

The first run restores packages before building and testing. With the checked-in settings, the suite starts the application and uses a local SQLite database. You do not need to start an application, database server or message broker yourself.

This recorded run had Playwright's Chromium browser installed and no broker configured:

```text
Failed:     0, Passed:    14, Skipped:     6, Total:    20
```

With the default settings, the browser test skips if Playwright's Chromium browser is missing. Your counts can differ with the checkout and configuration. Look for passing tests, expected skips and no failures. A result with zero tests does not confirm a successful run.

### 2. Read the skips

The example includes expected skips. Run the suite with detailed output to read each reason:

```bash
dotnet test samples/Northstar.ProtoTest --logger "console;verbosity=detailed"
```

Look for the broker journey. With no broker configured, its reason reads "No broker is configured; set ProtoTest:Messaging:Broker=container."

The four failure drills and the warning journey also skip by default. They deliberately fail or report a warning when enabled. Leave `ProtoTest:Sample:Drills` set to `false` for this lesson.

### 3. Find the evidence

In `samples/Northstar.ProtoTest/`, find the files under the build output:

```
bin/Debug/net8.0/TestResults/
    prototest-{runId}.prototrace    the steps ProtoTest recorded, as one file
    Northstar.ProtoTest/
        report.json             the verdict, as data
        report.html             the same verdict, as a page
```

Open `report.html` in a browser to see test results and route coverage. The `.prototrace` archive is called a trace. You will open it in the [viewer](https://trace.prototest.dev) in the third lesson.

## What happened

One command built the application, started it inside the test process, ran the suite, and wrote a trace and two reports.

The broker test requires a capability: something the configured framework can provide to a test. With no broker configured, that capability is absent. The test declares that it needs a broker, so ProtoTest skips it before its test body runs.

Other skips have different causes. The browser test checks whether its browser is available. The drills opt out until you enable them. Read the reason to tell these cases apart.

## Check yourself

<Checkpoint
  question="The run is green and the broker journey did not run. What reason did it give, and where did that reason come from?"
  verify={<>Run the test command with <code>--logger "console;verbosity=detailed"</code> and find the skipped test in the summary. The reason is printed beside it.</>}
>

`BrokerJourney` declares `[RequiresCapability(ProtoCapabilityKinds.Broker)]`. The sample's setup class registers the reason with `AddCapabilityReason`: "No broker is configured; set ProtoTest:Messaging:Broker=container."

ProtoTest checks for that capability and skips the test before its body runs when the capability is absent. [Add and remove an integration](/learn/good-tests/add-and-remove-an-integration) teaches `AddCapabilityReason`.

</Checkpoint>

## Remember

- One command runs the whole sample. The run leaves a trace, a JSON report and an HTML report.
- A skip can mean a missing requirement or a deliberately disabled test. Read its reason in the runner output.
- The same packages that build the sample go into your own project.

## Go deeper

- [Installation](/docs/getting-started/installation): the project template and packages for a project of your own.
- [Write your first test](/learn/start/write-your-first-test): the next lesson adds one test to the sample.
