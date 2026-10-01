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

You have run the sample suite. Now add a test that creates a project and checks the response: one request, two checks, one filtered run.

The sample already configures the application and its clients, so you write only the test.

## Do it

### 1. Add the file

Create `MyFirstJourney.cs` in `samples/Northstar.ProtoTest/` with the code below. The callouts explain the setup, request and checks.

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
    {line: 4, title: 'Use the package namespace', note: 'global:: starts namespace lookup at the root. This avoids confusing the ProtoTest packages with the enclosing Northstar.ProtoTest namespace.'},
    {line: 10, title: 'Select the application', note: 'Application selects the API configured in the sample setup class.'},
    {line: 11, title: 'Prepare a tenant and authentication', note: 'NorthstarMember groups tenant creation with an authenticator that sends the member token. The tenant provisioner also registers cleanup for teardown.'},
    {line: 14, title: 'Run through the NUnit adapter', note: 'This NUnit attribute marks the method as a test and wraps its lifecycle. The adapter creates and completes the test context, including setup and teardown.'},
    {line: 15, title: 'Declare the test identity', note: 'SignedInAs declares who the test acts as. With no role specified, the sample authenticator uses the tenant owner token.'},
    {line: 18, title: 'Include the test id in the name', note: 'The default generator gives each test in this host a different id. That does not guarantee unique names across separate runs.'},
    {line: 19, title: 'Retrieve the REST client', note: 'Rest() returns the client from this test context. The setup class supplies its address, so this test names no port.'},
    {line: 24, title: 'Check the HTTP status', note: 'HaveHttpStatus checks for 201 Created and records the comparison in the trace.'},
    {line: 25, title: 'Check the response fields', note: 'MatchShape checks name and status in the JSON body. A differing value produces a message with the JSON path, expected value and actual value.'},
  ]}
/>

### 2. Run it alone

From the repository root:

```bash
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~MyFirstJourney"
```

The filter selects your new test. You should see one passed test and no failures. The run also writes a trace, the archive of recorded work from the run.

Find it under `samples/Northstar.ProtoTest/`, in the same folder as in lesson 1:

```
bin/Debug/net8.0/TestResults/prototest-{runId}.prototrace
```

### 3. Run it again

Run the same command a second time. You should see one passed test again and a new trace file.

The project name contains `Proto.Context.TestId`, which usually differs between runs but is not guaranteed to.

The sample isolates project data in a tenant created for each test. Its tenant provisioner registers cleanup that removes the tenant and its projects at teardown. Repeatability depends on that isolation and cleanup, not on the project name changing.

## What happened

The example uses four parts of the configured sample:

- **Context.** The NUnit adapter creates a test context, the object behind `Proto.Context`. It holds this test's clients, state and attachments.
- **Client.** `Proto.Context.Rest()` retrieves the REST client for the selected application. The client knows the API address, so the test supplies a path.
- **Data.** `NorthstarMember` prepares a tenant and registers its cleanup. `[SignedInAs]` declares the identity that the sample authenticator uses for the request.
- **Checks.** The status check expects HTTP 201 Created. The shape check compares `name` and `status`, allowing other response fields. Both checks record their comparisons.

You wrote the request and its expected result. The existing setup class and attributes supply the application, client, identity and data cleanup.

## Check yourself

<Checkpoint
  question="Why can this test run again without depending on the project name changing?"
  verify={<>Run the filter twice. Both runs should pass. Find the attribute in the example that prepares the tenant and its cleanup.</>}
>

`NorthstarMember` prepares a tenant for each test and registers cleanup through its provisioner. The application keeps projects within that tenant, and teardown deletes its data.

The test id helps distinguish names within a run. Its random prefix does not guarantee uniqueness between runs, so it does not replace isolation or cleanup.

</Checkpoint>

## Remember

- This test sends one request and checks its status and two response fields.
- The client takes its address from the run, so the test names no port.
- The sample's tenant setup and cleanup keep test data separate. A generated name alone does not.

## Go deeper

- [Your first test](/docs/getting-started/first-test): the same path against an application of your own.
- Keep `MyFirstJourney.cs` for the next lesson, [Read the trace](/learn/start/read-the-trace). [Write your own attribute](/learn/extend/attributes) also reuses it.
- Next: [Read the trace](/learn/start/read-the-trace) breaks this test on purpose and finds out why.
