---
id: install-and-run
title: Install and run
sidebar_label: Install and run
sidebar_position: 1
description: "Run the Northstar.ProtoTest sample suite, find the trace and the report it leaves, and read the skip a missing capability produces."
---

import LearnShell from '@site/src/components/LearnShell';
import Link from '@docusaurus/Link';

# Install and run

Everything in this level runs against the Northstar.ProtoTest sample suite: one suite that composes an API, a database, a browser and a message broker over an in-process application.

<LearnShell
  level="Level 1, lesson 1"
  minutes="About 8 minutes"
  outcome={[
    'Run the sample suite on your machine.',
    'Find the trace and the reports it leaves behind.',
    'Read a skip and say what the run could not serve.',
  ]}
  before={[
    <>Level 0 (<Link to="/learn/why-integration-tests-get-hard/the-four-questions">the four questions</Link>).</>,
    'The .NET 8 SDK or newer, and git. The first run restores packages and takes a couple of minutes.',
  ]}
  situation={
    <>
      <p>To judge a test framework, watch it run a suite that touches an API, a database, a browser and a broker. The sample suite is small, it is green on an ordinary run, and it writes down everything it did.</p>
      <p>Some tests will not run: the broker journey needs a broker, and no broker is configured, and the four failure drills wait for an explicit opt-in. Those skips are part of the lesson, not a defect.</p>
    </>
  }
  checkpoint={{
    question:
      'The run is green and the broker journey did not run. What reason did it give, and where did that reason come from?',
    verify: (
      <>
        Run <code>dotnet test samples/Northstar.ProtoTest --logger "console;verbosity=detailed"</code> and read the runner output: the summary lists the skipped test and the reason beside it.
      </>
    ),
    reveal: (
      <>
        <code>BrokerJourney</code> declares <code>[RequiresCapability(ProtoCapabilityKinds.Broker)]</code>, and the composition registers the reason with <code>AddCapabilityReason</code>: "No broker is configured; set ProtoTest:Messaging:Broker=container." A capability the run cannot serve is absent, and the gated journey skips before its lifecycle starts. Level 2 adds the broker and watches the same journey run.
      </>
    ),
  }}
  learned={[
    'One command runs the whole sample; the run leaves a trace, a JSON report and an HTML report.',
    'A skip is a capability the run cannot serve, named in the runner output.',
    'The same packages that build the sample go into your own project.',
  ]}
  next={[
    {
      label: 'Write your first test',
      to: '/learn/one-test-one-journey/write-your-first-test',
      note: 'Add one journey to the sample, run it alone, and see it pass.',
    },
    {
      label: 'Installation',
      to: '/docs/getting-started/installation',
      note: 'The template and the package lines for a project of your own.',
    },
  ]}>

## Run the sample

From a terminal with git and the .NET SDK:

```bash
git clone https://github.com/MSeys/ProtoTest.git
cd ProtoTest
dotnet test samples/Northstar.ProtoTest
```

The ordinary run is green and fast. The application is hosted in-process, so the tests control its clock, and the store is a SQLite file that each run recreates. Two journeys can skip on a machine that is missing something:

- the broker journey, because no broker is configured;
- the browser journey, when Playwright's Chromium is not installed.

The four failure drills also skip in an ordinary run; `ProtoTest__Sample__Drills=true` lets them fail so you can read their traces. The warning journey beside them skips too. The summary prints how many tests passed, failed and skipped, and lists the skipped tests. Add `--logger "console;verbosity=detailed"` to print each reason beside its test.

A healthy run on a machine with Chromium ends exactly like this:

```text
Failed:     0, Passed:    14, Skipped:     6, Total:    20
```

The six skips are the broker journey, the four drills and the warning journey. Without Chromium the browser journey skips too, and the summary reads `Passed: 13, Skipped: 7`. If your clone reports `Total: 0`, it is behind the release; pull or clone again and run it once more.

```bash
git pull
```

## Find what the run left

The run writes three evidence files under the sample's output folder, next to the SQLite store it created for the run:

```
bin/Debug/net8.0/TestResults/Northstar.ProtoTest/
    northstar.prototrace    every operation, check, state change and artifact
    report.json             the machine-readable verdict, coverage and traffic
    report.html             the same report as a page you can open
```

Open `report.html` in a browser for the run's verdict and the routes it covered. Then download the trace and drop it on the [viewer](https://trace.prototest.dev): the run screen lists where the application ran and which capabilities the run composed, and each test opens into its own story.

## The same packages in your own project

The sample composes packages; so does a new project. The quickest start is the template. It targets `net10.0`, so it needs the .NET 10 SDK; on an older SDK, pass the framework the sample uses:

```bash
dotnet new install ProtoTest.Templates
dotnet new prototest -n Shop --framework net8.0
cd Shop
dotnet test
```

The template offers `net10.0`, `net9.0` and `net8.0`; `net8.0` matches the sample and the rest of this track. For an existing test project, add your runner package, `ProtoTest.Core` and one package per integration. The [installation page](/docs/getting-started/installation) lists every package and what arrives with it.

</LearnShell>
