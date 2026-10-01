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
    'The .NET 8 SDK or newer, and git.',
    'A couple of minutes for the first run, which restores packages.',
  ]}
/>

## The problem

Before you trust a test framework, you want to see it work. ProtoTest ships a sample suite called Northstar. It tests a small application through an API, a database, a browser and a message broker.

You run it once, see green, and look at what it wrote down.

## Do it

### 1. Run the suite

```bash
git clone https://github.com/MSeys/ProtoTest.git
cd ProtoTest
dotnet test samples/Northstar.ProtoTest
```

The first run is slow because it restores packages. The suite itself is fast. The application runs inside the test process, so nothing else needs to be installed or started.

A run on a machine with Chromium ends like this:

```text
Failed:     0, Passed:    14, Skipped:     6, Total:    20
```

Without Chromium, the browser test skips too, and the line reads `Passed: 13, Skipped: 7`. If your clone reports `Total: 0`, it is behind the release. Run `git pull` and try again.

### 2. Read the skips

Six tests did not run, and that is correct. Print the reason beside each one:

```bash
dotnet test samples/Northstar.ProtoTest --logger "console;verbosity=detailed"
```

Look for the broker journey. Its reason reads "No broker is configured; set ProtoTest:Messaging:Broker=container." The four failure drills and the warning journey also skip, because they fail or warn on purpose. Setting `ProtoTest__Sample__Drills=true` lets the drills run and fail.

### 3. Find the evidence

The run wrote its evidence next to the build output:

```
bin/Debug/net8.0/TestResults/
    prototest-{runId}.prototrace    every step the run took, as one file
    Northstar.ProtoTest/
        report.json             the verdict, as data
        report.html             the same verdict, as a page
```

Open `report.html` in a browser to see which tests passed and which routes they covered. The `.prototrace` file is the one you will use most. It is called a trace, and the next lessons read it in the [viewer](https://trace.prototest.dev).

## What happened

One command built the application, started it inside the test process, ran 20 tests, and wrote a trace and two reports.

The skips follow one rule: a test only runs when the run can provide what it needs. ProtoTest calls each thing a run can provide a capability. No broker is configured, so the run has no broker capability, and a test that needs one skips with a reason instead of failing. The skip is the run telling you what it could not serve.

## Check yourself

<Checkpoint
  question="The run is green and the broker journey did not run. What reason did it give, and where did that reason come from?"
  verify={<>Run the test command with <code>--logger "console;verbosity=detailed"</code> and find the skipped test in the summary. The reason is printed beside it.</>}
>

`BrokerJourney` declares `[RequiresCapability(ProtoCapabilityKinds.Broker)]`. The sample's setup registers the reason with `AddCapabilityReason`: "No broker is configured; set ProtoTest:Messaging:Broker=container." A capability the run cannot serve is absent, and the test skips before it starts. [Add and remove an integration](/learn/good-tests/add-and-remove-an-integration) teaches `AddCapabilityReason`.

</Checkpoint>

## Remember

- One command runs the whole sample. The run leaves a trace, a JSON report and an HTML report.
- A skip means the run could not provide something the test needs. The reason is in the runner output.
- The same packages that build the sample go into your own project.

## Go deeper

- [Installation](/docs/getting-started/installation): the project template and the package lines for a project of your own. The template targets `net10.0`, so on an older SDK pass `--framework net8.0`.
- [Write your first test](/learn/start/write-your-first-test): the next lesson adds one test to the sample.
