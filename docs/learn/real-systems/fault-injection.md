---
id: fault-injection
title: Inject faults on purpose
sidebar_label: Fault injection
sidebar_position: 4
description: "Replace a service for one test, follow failed notification deliveries into a dead-letter queue, and read a recorded billing regression."
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
    'Replace one service for a test and check recovery after publish failures',
    'Follow failed HTTP notifications through retries to a dead-letter queue',
    'Read a real regression from the trace the repository kept',
  ]}
  needs={['Lesson 3, Point the suite at a real stack', 'Optional: an OpenCSMS checkout and a container runtime. The kept trace is on this site.']}
/>

## The problem

A recovery test needs a repeatable failure. OpenCSMS tests inject publish errors and make an HTTP notification target return 503. They then check the retry result and stored invoice.

OpenCSMS commits an ended charging session and its event together. The event waits in an outbox, a database table of messages to publish. If publication fails, a dispatcher retries the stored message.

## Do it

### 1. Substitute the failing piece

An outbox test replaces the API's event publisher, the component that sends events to RabbitMQ, on a dedicated server owned by that test.
The substitute fails a chosen number of matching attempts, then delegates to the product's RabbitMQ publisher:

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
    {line: 3, title: 'Match the test\'s session', note: 'Other messages pass through untouched.'},
    {line: 9, title: 'Fail the first N', note: 'The first N matching publishes throw.'},
    {line: 16, title: 'Then travel the real path', note: 'A retry is a real publish through the product\'s publisher.'},
  ]}
  foot={<>From the suite, registered per test with <code>Proto.Context.Override&lt;IEventPublisher&gt;(publisher)</code>.</>}
/>

The predicate limits which messages fail. It does not hide the shared database's outbox rows from other dispatchers.

Two tests check the recovery:

- `AFailedPublishIsRetriedByTheDispatcherAndBillsOnce` fails one publish. It checks one pending outbox row, then after the retry one stored invoice and no pending row.
- `ABrokerOutageIsRiddenOutByMoreThanOneBackedOffRetry` fails two attempts inside an open transaction, so no other dispatcher takes the row. It checks two failures, a sent timestamp and one invoice.

From the OpenCSMS repository root, with a container runtime:

```bash
dotnet test tests/OpenCsms.Suite --filter "FullyQualifiedName~OutboxTests"
```

### 2. Keep a target down

The notification journeys use WireMock servers: fake HTTP endpoints the run starts in place of the real notification targets. One journey makes a target return 503 for notifications about its charging session.
Each 503 makes the notification consumer try again, by republishing the message to the same RabbitMQ queue. After three retries, it dead-letters the fourth failed delivery. That sends the notification to a queue for deliveries the consumer gave up on.

The test checks three things:

- The target saw four requests, every one a 503.
- The dead letter carries the event's own facts and `x-opencsms-retries: 3`.
- The session has one stored invoice with the expected id and total.

The rejection matches the session id in the request body, so other sessions still get the target's default 202.

### 3. Read a regression the repository kept

One fault was not injected. Billing used to read the tariff when the worker processed `session.ended`. So when an operator repriced a tariff while a car was still plugged in, the bill lost the idle fee for time already spent, and nothing reported it.

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



## What happened

The publisher override changes one test's API server. Successful retries still use the product's RabbitMQ path. The outbox tests check recovery after the injected failures, including one invoice for each tested session.

The HTTP-target tests check a different failure: rejected notification delivery after billing.

The historical trace records a pricing regression. The current implementation copies tariff terms into `SessionTariff` when the charging session starts. Billing reads that stored snapshot instead of the edited tariff. The journey still asserts five idle hours, a 10.00 idle fee and a 20.30 total.

Injection has limits. The injected publisher fails attempts but does not kill the broker. These tests need an in-process application and hosted workers, so the external modes skip them.

## Check yourself

<Checkpoint
  question="A fake rejects every notification for one invoice with 503. How many requests does the target see before the delivery is dead-lettered, and what does the dead letter carry?"
  verify={<>Read the three points under "Keep a target down" above. The journey <code>NotificationTargetOutagesAreDeadLettered</code> in the suite asserts that shape.</>}>

Four requests: the first attempt plus three retries through the queue. The dead letter carries the invoice and session ids, with `x-opencsms-retries: 3`. The test also checks that this session still has one stored invoice.

</Checkpoint>

## Remember

- Override a service on a test-owned server, and match the fault to that test's messages.
- Check both the failed delivery's retry history and the resulting product state.
- A kept trace is the evidence of a real regression.

Next: [Write your own attribute](/learn/extend/attributes).

## Go deeper

- [ProtoTrace](/docs/observability/prototrace): what the viewer reads, and what the archive cannot see.
