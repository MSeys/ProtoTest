---
id: fault-injection
title: Inject faults on purpose
sidebar_label: Fault injection
sidebar_position: 4
description: "Swap a run piece for a failing one, watch a 503 target reach the dead letter, and read the regression the repository kept."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';
import AnnotatedCode from '@site/src/components/AnnotatedCode';

# Inject faults on purpose

<Lesson
  track="Real systems"
  step="Lesson 4 of 4"
  minutes={9}
  outcomes={[
    'Swap one run piece for a failing one and pin the recovery',
    'Read a target that stays down through retries to the dead letter',
    'Read a real regression from the trace the repository kept',
  ]}
  needs={['Lesson 3, Point the suite at a real stack', 'Optional: an OpenCSMS checkout and a container runtime. The kept trace is on this site.']}
/>

## The problem

A dependency rarely fails at the moment your suite is watching. The broker rejects a publish. A webhook answers 503 to every call. Waiting for the environment to do that on demand does not work, so the suite causes it.

OpenCSMS ends a charging session transactionally: the ended session and its event commit together. A publish that fails is retried from the stored row. That design is only worth trusting if a failed publish is exercised on purpose.

## Do it

### 1. Substitute the failing piece

An outbox test replaces the API's event publisher for one test. The substitute fails a bounded number of matching attempts and delegates every later attempt to the product's own RabbitMQ publisher:

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

Two tests pin the recovery:

- `AFailedPublishIsRetriedByTheDispatcherAndBillsOnce` fails the request's own publish. The end still commits, the event is stored as one pending row, the dispatcher retries it, and the worker stores exactly one invoice.
- `ABrokerOutageIsRiddenOutByMoreThanOneBackedOffRetry` fails two attempts and keeps the event invisible to the dispatcher until both have happened. The stored row records both failed attempts, then reports itself sent, and the session is still billed once.

Run them with a container runtime:

```bash
dotnet test tests/OpenCsms.Suite --filter "FullyQualifiedName~OutboxTests"
```

### 2. Keep a target down

The notification journeys point the product at per-run WireMock fakes, which are fake HTTP servers. One journey makes the fake answer 503 for a single entity's notifications. The worker spends its three in-process retries and dead-letters the fourth attempt. Look for three things:

- The target saw four requests, every one a 503.
- The dead letter carries the event's own facts and `x-opencsms-retries: 3`.
- The invoice the target never accepted is still stored, exactly once.

The fake rejects by the session in the request body, because it is shared with notification journeys running in parallel. A fake that rejected everything would steal their deliveries.

A session the worker cannot bill follows the same shape. It produces a failure notification, and a target that stays down dead-letters that report with the reason the worker recorded. No invoice exists for that session.

### 3. Read a regression the repository kept

One fault was not injected. Billing used to read the tariff when the worker processed `session.ended`. An operator who repriced a tariff while a car was still plugged in silently removed the idle fee for time already spent.

The failing trace is kept at `docs/static/traces/opencsms-showpiece.prototrace`. It recorded two assertions that failed together:

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

Download the trace and open it in the [viewer](https://trace.prototest.dev):

<a href="pathname:///traces/opencsms-showpiece.prototrace">opencsms-showpiece.prototrace</a>

![The ProtoTrace viewer on the idle-fee failure: the failed test, the assertion message and the execution entry.](/images/opencsms/trace-viewer.png)

The viewer on the kept trace. The failing assertion and the operation that produced it are one story.

## What happened

A test can replace one piece of the run with its own failing one and keep the rest of the path real. The retry that follows is a real publish through the product's real broker, so the recovery you pin is the recovery the product has.

A target that stays down ends in a dead letter with a recorded retry count, and the product state stays intact. The kept trace is the evidence a fault leaves when it happens for real. The fix copies the tariff's terms onto the session when it starts, so a later reprice never reaches an open session. The journey now asserts the same numbers and passes in container mode.

Injection has limits:

- The injected publisher fails attempts. It does not kill the broker process.
- The fakes live inside the test process. A published or topology run cannot reach them, so those journeys skip there with their reason.
- A retry count is bounded by the product's own policy. The tests assert the count the product promises.

## Check yourself

<Checkpoint
  question="A fake rejects every notification for one invoice with 503. How many requests does the target see before the delivery is dead-lettered, and what does the dead letter carry?"
  verify={<>Read the three points under "Keep a target down" above. The journey <code>NotificationTargetOutagesAreDeadLettered</code> in the suite asserts that shape.</>}>

Four requests: the first attempt plus three in-process retries. The dead letter carries the invoice's ids and the completed-retry count in the `x-opencsms-retries` header, which reads 3. The invoice stays stored exactly once, because a rejected notification cannot roll back billing.

</Checkpoint>

## Remember

- A test can substitute one run piece for its own failing one and keep the rest of the path real.
- A target that stays down ends in a dead letter with a recorded retry count, and the product state stays intact.
- A kept trace is the evidence of a real regression.

Next: [Write your own attribute](/learn/extend/attributes).

## Go deeper

- [ProtoTrace](/docs/observability/prototrace): what the viewer reads, and what the archive cannot see.
