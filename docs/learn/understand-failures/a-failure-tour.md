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
  step="Lesson 1 of 8"
  minutes={10}
  outcomes={[
    'Run the sample suite with its four failing tests enabled',
    'Name what each failure is missing',
    'Say what the paired passing test changes',
  ]}
  needs={[
    <>The <Link to="/learn/start/install-and-run">Start track</Link> and <Link to="/learn/why-it-gets-hard">why integration tests get hard</Link></>,
    'The .NET SDK and the repository, to run the suite. The archives are on this site if you only want to read.',
  ]}
/>

## The problem

A failure you cannot reproduce is hard to trust. You want to see a real integration failure on demand, with the cause known in advance.

The Northstar sample suite ships four tests that fail on purpose. Next to each one sits a test that does the same journey and passes. The two differ by one habit, so you can see what that habit is worth.

## Do it

### 1. Turn the failing tests on

An ordinary run skips them, so the suite stays green. Set one environment variable and run the sample:

```powershell
$env:ProtoTest__Sample__Drills = "true"
dotnet test samples/Northstar.ProtoTest
```

Look for four failures, one for each of the four questions from the first lesson: time, state, environment and visibility. The paired tests pass. A fifth journey passes with a warning, so its trace records a partial outcome.

Each run writes a trace. You can also use the recorded ones below, which come from the same sample.

### 2. Open a card

Each card shows what a failing test recorded and what the test beside it does instead. The values are from a real recording, not an illustration.

<FailureGallery />

Look for one habit per pair. Time moves the clock instead of waiting. State creates the data it reads. Environment takes the address from the run. Visibility asserts the body the application sent.

### 3. Read one fix line by line

The time failure waits a real second, and the due window never closes. Its fix moves the test clock instead. This is the body of `TheTestClockClosesTheDueWindow`:

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
    {line: 1, title: 'Build and provision the invoice', note: 'Data() builds the request, the provisioner creates the invoice, and teardown removes it.'},
    {line: 3, title: 'Move the test clock', note: 'Advance records a clock.advance event, and the in-process application reads the same clock.'},
    {line: 4, title: 'Call through the composed client', note: 'Rest() takes the address from the run, so the same test works in-process or against a published environment.'},
    {line: 7, title: 'Assert the property the behavior depends on', note: 'MatchShape reports the JSON path and both values when it fails.'},
    {line: 11, title: 'Pay, then check the result', note: 'The second call reuses the same client, the same context and the same trace.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/FailureDrills.cs</code>. The failing test next to it waits on real time.</>}
/>

## What happened

Each failing test left one question unanswered, so something outside its control decided the result. The clock, the data, the address or the response body was left to chance.

The passing test keeps the journey and answers that one question. That is why one changed habit is enough. Every fix in the tour reads like the one above.

Each failure also left a trace. You can download any of them from its card and open it in the [viewer](https://trace.prototest.dev). The next lesson reads one.

## Check yourself

<Checkpoint
  question="The environment failure ran for about two seconds and its trace holds no HTTP request. What does the trace tell you?"
  verify={<>Download <a href="pathname:///lessons/l0-environment-drill.prototrace">l0-environment-drill.prototrace</a>, open it in the <a href="https://trace.prototest.dev">viewer</a>, and compare its execution phase with <a href="pathname:///lessons/l0-environment-fix.prototrace">l0-environment-fix.prototrace</a>.</>}>

The test called a raw `HttpClient` on `http://127.0.0.1:5099`, an address that belongs to one machine and sits outside the run. The trace records the connection error on the `test.execution` entry and no HTTP operation, because the run only records what it composes.

The fix calls the same endpoint through `Proto.Context.Rest()`, and then the request, the response and the shape check all appear.

</Checkpoint>

## Remember

- Each failing test fails because one of the four questions has no answer.
- The paired fix changes one habit: the clock, the data, the address or the assertion.
- Both halves leave a trace, so you can compare them.

## Go deeper

- [Read a failing trace](/learn/understand-failures/read-a-failing-trace): take one failure and read its check.
- [The trace reference](/docs/observability/prototrace): what a `.prototrace` records, operation by operation.
