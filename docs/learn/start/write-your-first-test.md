---
id: write-your-first-test
title: Write your first test
sidebar_label: Write your first test
sidebar_position: 2
description: "Add one test to the Northstar.ProtoTest sample suite, run it alone, and see it pass."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import Link from '@docusaurus/Link';

# Write your first test

<Lesson
  track="Start"
  step="Lesson 2 of 3"
  minutes={10}
  outcomes={[
    'Add a test file to the sample and run it by itself.',
    'Give the test its own data and check the response.',
    'Send the request through a client the run provides.',
  ]}
  needs={[
    <>The sample from <Link to="/learn/start/install-and-run">Run the sample suite</Link>, cloned and open in an editor.</>,
  ]}
/>

## The problem

The sample's tests cover every integration, so they are long. Your first test is smaller: one request, one check, one run of its own.

A test you can read from top to bottom is easy to trust. The steps are always the same. Pick the application, take a client, send one request, check what comes back.

## Do it

### 1. Add the file

Create `MyFirstJourney.cs` in `samples/Northstar.ProtoTest/`. This is the path in the sample's README. The test is a short class, so the callouts explain each line.

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
/>

### 2. Run it alone

From the repository root:

```bash
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~MyFirstJourney"
```

The filter selects one test. You should see one passed test and no failures. The run also writes a trace for it, in the same folder as in lesson 1:

```
bin/Debug/net8.0/TestResults/prototest-{runId}.prototrace
```

### 3. Run it again

Run the same command a second time. It passes again. The project name in the request is different each time, because it contains `Proto.Context.TestId`. Nothing the first run created gets in the way of the second.

## What happened

The test did four things, and each one came from the run instead of from your code.

- **Context.** `[ProtoTest]` gave the test a test context, the object behind `Proto.Context`. It holds the test's clients, its own state and its files.
- **Client.** `Proto.Context.Rest()` is a client, the object a test uses to talk to a system. It already knew the address of the API, so the test names a path and no port.
- **Data.** `NorthstarMember` created a tenant for this test before it ran and removed it afterwards. The test saw only its own data.
- **Checks.** The status check and the shape check each record what they compared. A shape check compares the whole response against the fields you list.

So a first test is a few attributes, one client call and one check. Everything else the run supplies.

## Check yourself

<Checkpoint
  question="The project name is built from Proto.Context.TestId. Run the test twice. What do the two runs share, and what differs?"
  verify={<>Run the filter twice and compare the two traces. The name in the request changes between the runs.</>}
>

The two runs share the test and its checks. The name differs, because `TestId` is unique for every test run. The test never reads a name another run left behind, so two runs cannot collide on data.

</Checkpoint>

## Remember

- A first test is a few attributes, one client call and one check.
- The client takes its address from the run, so the test names no port.
- Data named after `TestId` belongs to one test run.

## Go deeper

- [Your first test](/docs/getting-started/first-test): the same path against an application of your own.
- Keep `MyFirstJourney.cs` if you plan to do [Write your own attribute](/learn/extend/attributes), which reuses it. Otherwise delete the file. The suite is a fixture, and `git status` is clean without it.
- Next: [Read the trace](/learn/start/read-the-trace) breaks this test on purpose and finds out why.
