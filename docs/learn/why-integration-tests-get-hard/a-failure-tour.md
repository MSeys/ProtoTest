---
id: a-failure-tour
title: A failure tour
sidebar_label: A failure tour
sidebar_position: 2
description: "Four deliberate failures from the Learning demo, each read in its trace next to the test that does the same journey the right way."
---

import LearnShell from '@site/src/components/LearnShell';
import FailureGallery from '@site/src/components/FailureGallery';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import Link from '@docusaurus/Link';

# A failure tour

The Learning demo ships four tests that fail on purpose. Each one is paired with a test that runs the same journey the right way, so the difference between failing and holding is a habit, not a rewrite.

<LearnShell
  level="Level 0, lesson 2"
  minutes="About 10 minutes"
  outcome={[
    'Read a drill trace and name the question it failed to answer.',
    'Say what the paired test changed, and why that is enough.',
    'Record the four failures in your own run.',
  ]}
  before={[
    <>The four questions (<Link to="/learn/why-integration-tests-get-hard/the-four-questions">lesson 1</Link>).</>,
    'Nothing installed. The archives are on this site; running the suite needs the .NET SDK and the repository.',
  ]}
  situation={
    <>
      <p>A failure you cannot reproduce is hard to trust. The demo solves that by making the failures part of the suite: four tests that fail every time the drills are enabled, next to the four that pass.</p>
      <p>Set <code>ProtoTest__Sample__Drills=true</code>, and both halves run. The drills report their failure and leave their trace; the fixes run the same journey and stay green.</p>
    </>
  }
  checkpoint={{
    question:
      'The environment drill ran for about two seconds and its trace holds no HTTP request. What does the trace tell you?',
    verify: (
      <>
        Download <a href="pathname:///lessons/l0-environment-drill.prototrace">l0-environment-drill.prototrace</a>, open it in the{' '}
        <a href="https://trace.prototest.dev">viewer</a>, and compare its execution layer with the paired fix,{' '}
        <a href="pathname:///lessons/l0-environment-fix.prototrace">l0-environment-fix.prototrace</a>.
      </>
    ),
    reveal: (
      <>
        The drill called a raw <code>HttpClient</code> on <code>http://127.0.0.1:5099</code>, an address that belongs to one machine and sits outside the run. The trace records the connection error on the <code>test.execution</code> entry and no HTTP operation, because the run wraps what it composes. The fix calls the same endpoint through <code>Proto.Context.Rest()</code>, and the request, the response and the shape check all appear.
      </>
    ),
  }}
  learned={[
    'Each drill fails because one of the four questions has no answer.',
    'The paired fix changes one habit: the clock, the data, the address or the assertion.',
    'The trace is the evidence; the drill and the fix both leave one.',
  ]}
  next={[
    {
      label: 'What a test leaves behind',
      to: '/learn/why-integration-tests-get-hard/what-a-test-leaves-behind',
      note: 'Where the trace shows the state a test created, and the cleanup that removes it.',
    },
    {
      label: 'The trace reference',
      to: '/docs/observability/prototrace',
      note: 'What a .prototrace records, operation by operation.',
    },
  ]}>

## Four cards, four questions

Open a card to see what the drill recorded and what the test beside it does instead. The values are from a recording of the sample with the drills enabled, so they are what a reader would find, not an illustration.

<FailureGallery />

The four pairs match the four questions from lesson 1. Time moves the clock instead of waiting. State creates the data it reads. Environment takes the address from the composition. Visibility asserts the body the application sent.

## Record the failures in your own run

The drills run only when the suite is asked for them. In an ordinary run their bodies skip themselves, so the suite stays green.

```powershell
$env:ProtoTest__Sample__Drills = "true"
dotnet test samples/Northstar.ProtoTest
```

The run now reports four failures, one per question, and writes their traces. Each card above links its own archive from `docs/static/lessons/`, written by the same generator that writes the archive your run produces. Download one and drop it on the [viewer](https://trace.prototest.dev) to walk it yourself.

## One of the fixes, line by line

The time drill waits a real second. Its fix moves the test clock instead and then pays the invoice. This is the body of `TheTestClockClosesTheDueWindow`.

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
    {line: 7, title: 'Assert the property the behaviour depends on', note: 'MatchShape reports the JSON path and both values when it fails.'},
    {line: 11, title: 'Pay, then check the result', note: 'The second call reuses the same client, the same context and the same trace.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/FailureDrills.cs</code>. The drill next to it waits on real time and the due window never closes.</>}
/>

Every fix in the tour reads like this one: the journey stays, and one answer changes.

</LearnShell>
