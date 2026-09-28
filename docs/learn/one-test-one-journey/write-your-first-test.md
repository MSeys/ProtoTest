---
id: write-your-first-test
title: Write your first test
sidebar_label: Write your first test
sidebar_position: 2
description: "Add one journey to the Northstar sample, run it alone, and see it pass."
---

import LearnShell from '@site/src/components/LearnShell';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import Link from '@docusaurus/Link';

# Write your first test

The sample has journeys for every integration. Your own first test is smaller: one request, one shape, one run of its own.

<LearnShell
  level="Level 1, lesson 2"
  minutes="About 10 minutes"
  outcome={[
    'Add a test file to the sample and run it by itself.',
    'Give the test its own data and assert the response with a shape.',
    'Point the test at a client the run composed.',
  ]}
  before={[
    <>Install and run (<Link to="/learn/one-test-one-journey/install-and-run">lesson 1</Link>).</>,
    'The repository cloned and open in an editor.',
  ]}
  situation={
    <>
      <p>A test is easiest to trust when it is one journey you can read top to bottom. The sample's tests are written that way, and a new one starts the same: select the application, take a client from the context, send one request, assert what comes back.</p>
      <p>This lesson follows the path in the sample's README, so the file you write matches the archive the next lesson reads.</p>
    </>
  }
  checkpoint={{
    question:
      'The project name is built from Proto.Context.TestId. Run the test twice. What do the two runs share, and what differs?',
    verify: (
      <>
        Run the filter twice and compare the two traces. The name in the request changes between the runs.
      </>
    ),
    reveal: (
      <>
        <code>TestId</code> is unique for every test run, so the name is unique too. The test never reads a name another run left behind. That is the state answer from Level 0, made concrete in one line.
      </>
    ),
  }}
  learned={[
    'A first test is four attributes, one client call and one assertion.',
    'The name comes from the test id, so two runs cannot collide.',
    'The run leaves a trace for the test you wrote, not only for the sample.',
  ]}
  next={[
    {
      label: 'Read the trace',
      to: '/learn/one-test-one-journey/read-the-trace',
      note: 'Walk the trace of this journey layer by layer, and read a failure message that names the fix.',
    },
    {
      label: 'Your first test',
      to: '/docs/getting-started/first-test',
      note: 'The same path against an application of your own.',
    },
  ]}>

## 1. Add the file

Create `MyFirstJourney.cs` in `samples/Northstar.ProtoTest/`:

<AnnotatedCode
  filename="MyFirstJourney.cs"
  code={`namespace Northstar.ProtoTest;

using System.Net;
using global::ProtoTest.Core;
using global::ProtoTest.Http;
using global::ProtoTest.NUnit;
using global::ProtoTest.Rest;
using global::ProtoTest.SampleApp.Contracts;

[Application(NorthstarTargets.Api)]
[NorthstarMember]
public sealed class MyFirstJourney
{
    [ProtoTest]
    [SignedInAs]
    public async Task CreatingAProjectReturnsIt()
    {
        var name = $"first-{Proto.Context.TestId}";
        using var created = await Proto.Context.Rest()
            .Body(new CreateProjectRequest(name))
            .PostAsync("/api/v1/projects");

        created
            .Should.HaveHttpStatus(HttpStatusCode.Created)
            .Should.MatchShape(new { name, status = ProjectStatuses.Active });
    }
}`}
  callouts={[
    {line: 4, title: 'Name the package namespaces with global::', note: 'The file lives in Northstar.ProtoTest, so a plain using would resolve ProtoTest to the sample namespace. global:: names the package namespace without ambiguity.'},
    {line: 10, title: 'Select the application', note: 'Application picks the API the run composed for this test.'},
    {line: 11, title: 'Provision the tenant and sign in', note: 'NorthstarMember is a composite: an isolated tenant for this test, and an authenticator that carries the member token. The tenant is removed at teardown.'},
    {line: 14, title: 'Start the context', note: '[ProtoTest] is the runner attribute that creates the execution context around the test body and completes it afterwards.'},
    {line: 18, title: 'Name the data after the test', note: 'TestId is unique per run, so the name cannot collide with another test.'},
    {line: 19, title: 'Take the client from the context', note: 'Rest() carries the address the run composed; the test does not know a port.'},
    {line: 25, title: 'Assert what the response is', note: 'The status check and the shape check both record what they read, and the shape check reports the JSON path and both values when it fails.'},
  ]}
  foot={<>The path from <code>samples/Northstar.ProtoTest/README.md</code>. The next lesson reads the trace this test writes.</>}
/>

## 2. Run it alone

From the repository root:

```bash
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~MyFirstJourney"
```

The filter runs one test. The run is green, and it writes the same three files as the full suite, under the sample's output folder:

```
bin/Debug/net8.0/TestResults/Northstar.ProtoTest/northstar.prototrace
```

This is the same journey as `l1-first-journey.prototrace`, the archive the next lesson walks, so your run and the annotated one line up.

## 3. Remove the file when you are done

```bash
rm MyFirstJourney.cs
```

Run that from the sample folder, or delete the file in your editor. The suite is a fixture, not a scratchpad. Deleting the file leaves the repository unchanged, and `git status` is clean. If you want to keep the test while you work through the level, keep it; the sample's own journeys do not change.

</LearnShell>
