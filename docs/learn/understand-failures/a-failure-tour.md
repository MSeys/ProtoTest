---
id: a-failure-tour
title: Run four failures on purpose
sidebar_label: A failure tour
sidebar_position: 1
description: "Run the four deliberate failures in the Northstar sample suite and see what each one is missing, next to the test that does the same journey right."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';
import FailureGallery from '@site/src/components/FailureGallery';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import Link from '@docusaurus/Link';

# Run four failures on purpose

<Lesson
  track="Understand failures"
  step="Lesson 1 of 9"
  minutes={10}
  outcomes={[
    'Run the sample suite with its four failing tests enabled',
    'Identify the assumption behind each failure',
    'Say what the paired passing test changes',
  ]}
  needs={[
    <>The <Link to="/learn/start/install-and-run">Start track</Link> and <Link to="/learn/why-it-gets-hard">why integration tests get hard</Link></>,
    'The SDK, runtime and repository from the Start track, with the default local sample configuration. You can also read the saved traces without running the suite.',
  ]}
/>

## The problem

A failure you cannot reproduce is hard to trust. You want to see a real integration failure on demand, with the cause known in advance.

The Northstar sample suite ships four deliberate failures, each paired with a passing example. Compare them to see how the test controls time, creates data, chooses an address or checks an error response.

## Do it

### 1. Turn the failing tests on

The sample skips the deliberate failures unless you enable them. Use the default local configuration, with nothing listening on port 5099.

Set one environment variable and run the sample:

```powershell
$env:ProtoTest__Sample__Drills = "true"
dotnet test samples/Northstar.ProtoTest
```

Remove `ProtoTest__Sample__Drills` again before running the suite normally.

With those prerequisites, expect four deliberate failures: time, state, environment and visibility. Their paired examples should pass. A fifth journey adds a finding and calls `Assert.Warn`, so its trace records a partial outcome.

You can also use the saved traces below, which come from the same sample.

### 2. Open a card

Each card shows what a failing test recorded and what the test beside it does instead.

<FailureGallery />

Look for one habit per pair. Time moves the clock instead of waiting. State creates the data it reads. Environment takes the address from the run. Visibility checks the expected error status and body.

The visibility failure already includes the response body in its status error. Its paired test checks that the empty name produces HTTP 400 and the expected validation error.

### 3. Read one fix line by line

The time failure waits a real second, which does not advance the test clock. Its paired test advances that clock eight days, then calls the application to observe the overdue invoice.

Moving the test clock alone does not run billing. The next request does.

This is the body of `TheTestClockClosesTheDueWindow`:

<AnnotatedCode
  filename="FailureDrills.cs"
  code={`var invoice = await Proto.Context.Data().IssueInvoiceAsync();

Proto.Context.Clock.Advance(TimeSpan.FromDays(8));
using var organization = await Proto.Context.Rest().GetAsync("/api/v1/organization");
organization
    .Should.HaveHttpStatus(HttpStatusCode.OK)
    .Should.MatchShape(new { status = SubscriptionStatuses.PastDue });

using var paid = await Proto.Context.Rest()
    .Body(new PayInvoiceRequest(PaymentMethods.Visa))
    .PostAsync("/api/v1/invoices/{invoiceId}/pay", new { invoiceId = invoice.Id });
paid
    .Should.HaveHttpStatus(HttpStatusCode.OK)
    .Should.MatchShape(new { status = InvoiceStatuses.Paid });`}
  callouts={[
    {line: 1, title: 'Build and provision the invoice', note: 'The local provisioner records usage and advances the tenant clock to issue an invoice. Tenant teardown removes the data.'},
    {line: 3, title: 'Move the test clock', note: 'Advance moves the test\'s clock and records a clock.advance event. The application runs inside the test process and reads that same clock.'},
    {line: 4, title: 'Call through the composed client', note: 'Rest() uses the client supplied by the run. Here it calls the in-process application, which reads the test clock.'},
    {line: 7, title: 'Assert the property the behavior depends on', note: 'For a status value mismatch, MatchShape reports the JSON path, expected value and actual value.'},
    {line: 11, title: 'Pay, then check the result', note: 'The second call reuses the same client, the same context and the same trace.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/FailureDrills.cs</code>. The failing test next to it waits on real time.</>}
/>

## What happened

Each deliberate failure exposes an assumption. Real time moves the test clock, a hardcoded project exists, a local port serves the application, or an invalid request succeeds.

The paired examples replace those assumptions with explicit setup, the client supplied by the run, and checks of the expected response. Compare both the requests and the assertions when reading each pair.

Each failure left a trace you can download from its card and open in the [viewer](https://trace.prototest.dev). The next lesson reads one.

## Check yourself

<Checkpoint
  question="The saved environment failure ran for about two seconds and its trace holds no HTTP request. What can you learn from the trace and source together?"
  verify={<>Download <a href="pathname:///lessons/l0-environment-drill.prototrace">l0-environment-drill.prototrace</a>, open it in the <a href="https://trace.prototest.dev">viewer</a>, and compare its execution phase with <a href="pathname:///lessons/l0-environment-fix.prototrace">l0-environment-fix.prototrace</a>.</>}>

The saved trace records a connection error on `test.execution` and no HTTP operation. The source explains why: this test uses a raw `HttpClient` with the hardcoded address `http://127.0.0.1:5099`.

That client has no HTTP instrumentation, so its request is absent from the trace. A missing operation alone does not prove that no request happened.

The fix calls the same endpoint through `Proto.Context.Rest()`, and then the request, the response and the shape check all appear.

</Checkpoint>

## Remember

- Each deliberate failure exposes an assumption about time, state, the environment or the expected response.
- The paired fix changes one habit: the clock, the data, the address or the assertion.
- Both halves leave a trace, so you can compare them.

## Go deeper

- [Read a failing trace](/learn/understand-failures/read-a-failing-trace): take one failure and read its check.
- [The trace reference](/docs/observability/prototrace): what a `.prototrace` records, operation by operation.
