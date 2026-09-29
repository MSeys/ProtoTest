---
id: fault-injection
title: Inject faults on purpose
sidebar_label: Fault injection
sidebar_position: 4
description: "Swap a run piece for a failing one, watch a 503 target reach the dead letter, and read the regression the repository kept."
---

import LearnShell from '@site/src/components/LearnShell';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import Link from '@docusaurus/Link';

# Inject faults on purpose

A dependency that goes down at the wrong moment is the failure a suite rarely sees: the broker rejects a publish, a webhook answers 503 every time it is called. Waiting for the environment to produce that on demand does not work. The suite injects it instead.

<LearnShell
  level="Level 5, lesson 4"
  minutes="About 9 minutes"
  outcome={[
    'Swap one run piece for a failing one and pin the recovery.',
    'Read a target that stays down through retries to the dead letter.',
    'Read a real regression from the trace the repository kept.',
  ]}
  before={[
    <>Point the suite at a real stack (<Link to="/learn/real-topology/published-mode">lesson 3</Link>).</>,
    'An OpenCSMS checkout and a container runtime for the runs. The kept trace is on this site.',
  ]}
  situation={
    <>
      <p>The outbox makes ending a session transactional: the ended session and its event commit together, and a publish that fails is retried from the stored row. That design is only worth trusting if a failed publish is exercised on purpose.</p>
      <p>The same goes for the notification path. One real regression also happened, and the repository keeps its failing trace as the evidence.</p>
    </>
  }
  checkpoint={{
    question:
      'A fake rejects every notification for one invoice with 503. How many requests does the target see before the delivery is dead-lettered, and what does the dead letter carry?',
    verify: (
      <>
        Read <code>NotificationTargetOutagesAreDeadLettered</code> in the suite and the assertions at the end of the journey.
      </>
    ),
    reveal: (
      <>
        Four requests: the first attempt plus three in-process retries. The dead letter carries the invoice's ids and the completed-retry count in the <code>x-opencsms-retries</code> header, which reads 3. The invoice stays stored exactly once, because a rejected notification cannot roll back billing.
      </>
    ),
  }}
  learned={[
    'A test can substitute one run piece for its own failing one and keep the rest of the path real.',
    'A target that stays down ends in a dead letter with a recorded retry count, and the product state stays intact.',
    'The regression trace is the evidence a fault leaves when it happens for real, and the repository keeps it.',
  ]}
  next={[
    {
      label: 'Write your own attribute',
      to: '/learn/make-it-yours/attributes',
      note: 'Level 6 starts from the sample again and turns setup into named capabilities.',
    },
    {
      label: 'ProtoTrace',
      to: '/docs/observability/prototrace',
      note: 'What the viewer reads, and what the archive cannot see.',
    },
  ]}>

## The fault is a substitution

The outbox tests replace the API's event publisher for one test. The substituted publisher fails a bounded number of matching attempts and delegates every later attempt to the product's own RabbitMQ publisher:

<AnnotatedCode
  filename="FailFirstAttemptsEventPublisher.cs"
  code={`public async ValueTask PublishAsync<T>(string routingKey, T message, CancellationToken cancellationToken = default)
{
    if (_matches is not null && !_matches(routingKey, message))
    {
        await _publisher.PublishAsync(routingKey, message, cancellationToken);
        return;
    }

    if (Interlocked.Increment(ref _attempts) <= _failures)
    {
        Interlocked.Increment(ref _observedFailures);
        throw new InvalidOperationException(
            $"Publish attempt {Attempts} for '{routingKey}' failed (injected for this test).");
    }

    await _publisher.PublishAsync(routingKey, message, cancellationToken);
}`}
  callouts={[
    {line: 3, title: 'Narrow the fault to this test', note: 'The match predicate keeps another test\'s pending row on the real path, so parallel tests do not spend each other\'s failure budget.'},
    {line: 9, title: 'Fail a bounded number of attempts', note: 'The first N matching publishes throw. The count is the outage the test wants to survive.'},
    {line: 16, title: 'Then travel the real path', note: 'Every attempt after the budget delegates to the product\'s own publisher, so a retry is a real publish, not a mock.'},
  ]}
  foot={<>From the suite, registered per test with <code>Proto.Context.Override&lt;IEventPublisher&gt;(publisher)</code>.</>}
/>

The two tests pin the recovery:

- `AFailedPublishIsRetriedByTheDispatcherAndBillsOnce` fails the request's own publish. The end still commits, the event is stored as one pending row, the dispatcher retries it, and the worker stores exactly one invoice.
- `ABrokerOutageIsRiddenOutByMoreThanOneBackedOffRetry` fails two attempts and keeps the event invisible to the dispatcher until both have happened. The stored row records both failed attempts, then reports itself sent, and the session is still billed once.

Run them with a container runtime:

```bash
dotnet test tests/OpenCsms.Suite --filter "FullyQualifiedName~OutboxTests"
```

## The target that stays down

The notification journeys point the product at per-run WireMock fakes, and one journey makes the fake answer 503 for a single entity's notifications. The worker spends its three in-process retries and dead-letters the fourth attempt:

- the target saw four requests, every one a 503;
- the dead letter carries the event's own facts and `x-opencsms-retries: 3`;
- the invoice the target never accepted is still stored, exactly once.

The rejection is keyed to the session in the request body, because the fake is shared with the notification journeys that run in parallel beside it. A fake that rejected everything would steal their deliveries.

The billing failure path follows the same shape: a session the worker cannot bill produces a failure notification, and a target that stays down dead-letters that report with the reason the worker recorded. No invoice exists for that session.

## The regression that was kept

One fault was not injected. Billing used to read the tariff at the moment the worker processed `session.ended`, so an operator who repriced a tariff while a car was still plugged in silently removed the idle fee for time already spent.

The failing trace is kept at `docs/static/traces/opencsms-showpiece.prototrace` and served at `/traces/opencsms-showpiece.prototrace`. It recorded the two assertions that failed together:

```text
FAILED OpenCsms.Suite.Journeys.IdleFeeAfterTariffChange.TheIdleFeeStillAppliesAfterAReprice (496 ms)
  the idle fee the session started under still applies
  Assert.That(stored.IdleFeeAmount, Is.EqualTo(10.00m))
    Expected: 10m
    But was:  0m
  22 kWh at the tariff the session started under, the start fee and the idle fee
  Assert.That(stored.Total, Is.EqualTo(20.30m))
    Expected: 20.3m
    But was:  13.6m
```

The fix copies the tariff's terms onto the session when it starts, so a later reprice never reaches an open session. The journey now asserts the same numbers and passes in container mode.

![The ProtoTrace viewer on the idle-fee failure: the failed test, the assertion message and the execution entry.](/images/opencsms/trace-viewer.png)

The viewer on the kept trace. The failing assertion and the operation that produced it are one story, which is the point of keeping the file.

Download the trace and open it in the [viewer](https://trace.prototest.dev):

<a href="pathname:///traces/opencsms-showpiece.prototrace">opencsms-showpiece.prototrace</a>

## What injection does not cover

- The injected publisher fails attempts; it does not kill the broker process. A broker that dies under a live consumer is the environment's fault to produce.
- The fakes live inside the test process. A published or topology run cannot reach them, so those journeys skip there with their reason.
- A retry count is bounded by the product's own policy. The tests assert the count the product promises, not an unlimited recovery.

</LearnShell>
