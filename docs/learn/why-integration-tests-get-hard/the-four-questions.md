---
id: the-four-questions
title: The four questions
sidebar_label: The four questions
sidebar_position: 1
description: "The four questions every integration test has to answer (time, state, environment, visibility), and where each one shows up in a trace."
---

import LearnShell from '@site/src/components/LearnShell';
import FailureGallery from '@site/src/components/FailureGallery';
import AnnotatedCode from '@site/src/components/AnnotatedCode';

# The four questions

An integration test talks to the parts of a system that actually run: an API, a database, a broker, a browser, a clock you do not own. That is what makes it valuable, and it is also why it fails in ways a unit test never does.

Before you can trust a test like that, it has to answer four questions.

1. **Time.** What moves the clock: the test, the application, or a real wait?
2. **State.** What does the test share with other tests, and what does it leave behind?
3. **Environment.** Where does the application run, and where does its address come from?
4. **Visibility.** When the test fails, what can you see about what happened?

<LearnShell
  level="Level 0, lesson 1"
  minutes="About 15 minutes"
  outcome={[
    'Name the four failure modes and recognize them in a suite you already have.',
    'Point at the part of a trace that answers each question.',
    'Say what a passing test should leave behind.',
  ]}
  before={[
    'Nothing from this track.',
    <>To run the demo yourself, <code>dotnet test samples/Northstar.ProtoTest</code> needs the .NET SDK and the repository. You can also follow the lesson by reading the traces in the <a href="https://trace.prototest.dev/?demo=1">viewer</a>.</>,
  ]}
  checkpoint={{
    question:
      'The time drill waits one real second and the application still reports active. Why does the wait not close the due window?',
    verify: (
      <>
        Open the <a href="https://trace.prototest.dev/?demo=1">demo trace</a> and follow one failing check down to
        the values that differed. When you can explain why the test failed without running it again, you have the
        habit the rest of this track builds on.
      </>
    ),
    reveal: (
      <>
        The application runs on the test clock, and real time does not move it. The billing window stays open
        until something advances that clock. The fix moves it from the test side with{' '}
        <code>Proto.Context.Clock.Advance</code>, so the application and the assertion read the same moment.
      </>
    ),
  }}
  next={[
    {label: 'The reference', to: '/docs/', note: 'What each integration does and how to configure it.'},
    {label: 'Level 0', to: '/learn/', note: 'The flakiness taxonomy continues the level: the same four questions, sorted into the failure modes that show up in CI.'},
  ]}>

The lesson follows four pairs of tests from the Learning demo. Each pair runs the same journey twice: once the way that fails, once the way that holds. Open a card to see what the trace recorded in the drill, and what the test beside it does instead.

<FailureGallery />

Set `ProtoTest__Sample__Drills=true`, run the suite, and the four failures are recorded next to the tests that fix them. Each failure becomes the checkpoint for this lesson: open its trace, find the question it failed to answer, and name what the test that holds changed.

### One of the fixes, line by line

The time drill waits a real second and the due window never closes. Its fix moves the test clock instead. This is the body of `TheTestClockClosesTheDueWindow`.

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

</LearnShell>
