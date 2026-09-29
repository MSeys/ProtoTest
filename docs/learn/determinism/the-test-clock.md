---
id: the-test-clock
title: Move the test clock
sidebar_label: Move the test clock
sidebar_position: 1
description: "Move a test's clock past a billing boundary instead of waiting on real time, and read the clock event in the trace."
---

import LearnShell from '@site/src/components/LearnShell';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import Link from '@docusaurus/Link';

# Move the test clock

A billing period takes a month. This test closes one in about 230 ms. The application runs on the test clock, and the test moves it.

<LearnShell
  level="Level 3, lesson 1"
  minutes="About 8 minutes"
  outcome={[
    'Move a test clock instead of waiting on real time.',
    'Explain how the in-process application reads the same clock.',
    'Find the clock event and the period close in the trace.',
  ]}
  before={[
    <>Level 2 (<Link to="/learn/compose-dont-glue/when-not-to-compose">when not to compose</Link>).</>,
    'The sample cloned. Reading the archive alone also works.',
  ]}
  situation={
    <>
      <p>A subscription renews every billing period. A test for the due window has to place the application a period later, and a test that sleeps through that time is slow on a good day and wrong on a bad one.</p>
      <p>The sample hosts the API in-process, and that server hands the application the test's clock. <code>ClockJourney</code> asks the application where the period ends, moves the clock just past that instant, and checks the invoice the application issued.</p>
    </>
  }
  checkpoint={{
    question:
      'The clock advance in the trace reads 30:0:00:01. Why does the test move the clock to the period end minus the current time plus one second, instead of a fixed number of days?',
    verify: (
      <>
        Download <a href="pathname:///lessons/l3-clock-window.prototrace">l3-clock-window.prototrace</a>, open it in the{' '}
        <a href="https://trace.prototest.dev">viewer</a>, select the test, and find the <code>clock.advance</code> event on the test execution.
      </>
    ),
    reveal: (
      <>
        The period end belongs to the application's billing logic, so the test reads it instead of assuming a month length. One second past that instant closes the period, and the assertion that follows compares the invoice stamp with the same moment the application computed.
      </>
    ),
  }}
  learned={[
    'The test owns the clock; the in-process application reads it through its time provider.',
    'Moving the clock is instant, and the trace records the delta and both instants.',
    'A wait on real time changes nothing the application can see.',
  ]}
  next={[
    {
      label: 'Wait for readiness, not for time',
      to: '/learn/determinism/readiness-instead-of-sleeps',
      note: 'How a run waits for the address it started, and what it records while waiting.',
    },
    {
      label: 'Test time',
      to: '/docs/foundation/time',
      note: 'The clock API, the bridge into the application, and the limits.',
    },
  ]}>

## One clock, two readers

The composition starts the API in-process. That server is what carries the test clock into the application:

<AnnotatedCode
  filename="Setup.cs"
  code={`builder.AddApplication(NorthstarTargets.Api, app =>
{
    if (run.RunsLocalApplications)
    {
        // The in-process server is what carries the test clock into the application.
        app.AddAspNetCoreServer<NorthstarProgram>(configureWebHost: webHost =>
            ConfigureHostedApplication(webHost, run));
    }`}
  callouts={[
    {line: 1, title: 'The API application', note: 'Tests select it with [Application(NorthstarTargets.Api)].'},
    {line: 3, title: 'Only when the run hosts the application', note: 'A run pointed at a deployed address does not start a server, and it cannot move that application time.'},
    {line: 6, title: 'The bridge', note: 'The in-process server replaces the application\'s time provider with the run\'s, so the application computes every stamp and period from the test clock.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/Setup.cs</code>. The suite also removes the domain's own time provider in its test-side composition, so the bridge wins and the application keeps the test clock.</>}
/>

The test then moves that clock and reads what the application did with it.

## The journey

<AnnotatedCode
  filename="ClockJourney.cs"
  code={`using var subscription = await Proto.Context.Rest().GetAsync("/api/v1/subscription");
var current = subscription
    .Should.HaveHttpStatus(HttpStatusCode.OK)
    .ReadRequired<SubscriptionResponse>();

// Move the test clock past the period end; the application reads the same clock.
Proto.Context.Clock.Advance(
    current.CurrentPeriodEndUtc - Proto.Context.Clock.GetUtcNow() + TimeSpan.FromSeconds(1));

using var invoices = await Proto.Context.Rest()
    .GetAsync("/api/v1/invoices", new { status = InvoiceStatuses.Open });
var invoice = invoices
    .Should.HaveHttpStatus(HttpStatusCode.OK)
    .ReadRequired<CursorPage<InvoiceResponse>>()
    .Items
    .Single();`}
  callouts={[
    {line: 1, title: 'Ask the application for the window', note: 'The period end is the application\'s value, read over the composed client.'},
    {line: 7, title: 'Move the test clock', note: 'The delta is the time left in the period plus one second, so the clock lands just past the boundary.'},
    {line: 10, title: 'Read what the move produced', note: 'The application issued the invoice when the period closed, and the test reads the open one.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/ClockJourney.cs</code>. The test asserts the invoice stamp and its line afterwards with ordinary NUnit assertions.</>}
/>

## What the trace recorded

From the archive:

| Entry | What it shows |
| --- | --- |
| `clock.advance` on `test.execution`, "Clock advanced by 30:0:00:01" | the delta, from the test side |
| `http.request` REST `GET /api/v1/subscription`, 141.9 ms, HTTP 200 | the period the test read |
| `Northstar.Domain` `invoice.issue`, reported by the application | the application closed the period |
| `http.request` REST `GET /api/v1/invoices`, 68.4 ms, HTTP 200 | the invoice the test read |
| `test.execution`, 233.7 ms | the whole journey |

The clock advance is an event on the test execution, not a request. Nothing was sent anywhere to move time: the test holds the clock, and the application reads it.

## When the application does not run in-process

A loopback, container, AppHost or deployed application resolves its own time provider. The run cannot move that time, and a journey that needs the clock is an in-process journey. That is why the sample runs the API in-process for these tests and points the browser journey at a loopback instance. The [test time reference](/docs/foundation/time) names the attributes that skip a clock-dependent test when the run cannot serve it.

</LearnShell>