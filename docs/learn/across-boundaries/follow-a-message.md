---
id: follow-a-message
title: Follow a message through a broker
sidebar_position: 5
description: "Trigger an event through the API and await the message the application publishes."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';

# Follow a message through a broker

<Lesson
  track="Across boundaries"
  step="Lesson 5 of 6"
  minutes={7}
  outcomes={[
    'Await a published message with a predicate and a timeout',
    'Assert on the message you received',
    'Read what a run reports when no broker is available',
  ]}
  needs={[
    <>The previous lesson, <a href="./drive-the-browser">Drive the browser with a page object</a></>,
  ]}
/>

## The problem

When a user pays an invoice, the application publishes an event for other services. The API response does not show it. A test that calls `Thread.Sleep` and then looks is slow when the message is early and wrong when it is late.

## Do it

### 1. Run the test alone

```bash
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~PayingAnInvoicePublishesAnInvoicePaidEvent"
```

On a plain run the result is one skipped test. Step 5 explains why.

### 2. Trigger the event

```csharp
var invoice = await Proto.Context.Data().IssueInvoiceAsync();

using var paid = await Proto.Context.Rest()
    .Body(new PayInvoiceRequest(PaymentMethods.Visa))
    .PostAsync("/api/v1/invoices/{invoiceId}/pay", new { invoiceId = invoice.Id });
paid.Should.HaveHttpStatus(HttpStatusCode.OK);
```

The test issues an invoice through the data helper, then pays it over REST. Paying makes the application publish an `invoice.paid` event.

### 3. Await the message

```csharp
var message = await Proto.Context.Messaging().AwaitAsync(
    "invoice.paid",
    candidate => candidate.Payload is not null
        && candidate.Payload.Contains(
            $"\"id\":{invoice.Id.ToString(CultureInfo.InvariantCulture)}",
            StringComparison.Ordinal),
    TimeSpan.FromSeconds(15));
```

`Proto.Context.Messaging()` is the broker client. `AwaitAsync` takes the destination, a predicate and a timeout. It returns the first message on `invoice.paid` for which the predicate is true. The predicate matches this test's invoice id, so a message from another test running in parallel cannot satisfy it.

If no matching message arrives within 15 seconds, the call throws a `TimeoutException` and the test fails. Nothing sleeps.

### 4. Assert the message

```csharp
using (Assert.EnterMultipleScope())
{
    Assert.That(message.ContentType, Is.EqualTo("application/json"));
    Assert.That(message.Payload, Does.Contain($"\"status\":\"{InvoiceStatuses.Paid}\""));
}
```

### 5. See what happens without a broker

The class carries `[RequiresCapability(ProtoCapabilityKinds.Broker)]`. A real broker makes the `Broker` capability true. The sample's setup class adds a broker only when you ask for one, so on a plain run the capability is absent and the test skips. The setup class also gives the reason: "No broker is configured; set ProtoTest:Messaging:Broker=container."

To run it, start Docker and set the variable the message names:

```powershell
$env:ProtoTest__Messaging__Broker = "container"
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~PayingAnInvoicePublishesAnInvoicePaidEvent"
```

The run then starts a RabbitMQ container, and the test runs against it instead of skipping.

## What happened

The test declared its tap in advance. The setup class calls `.Declare("invoice.paid")` and `.Tap("invoice.paid")`, so the host binds each test's listener during setup, before the payment happens. A message published a moment later cannot be missed.

Without an adapter, ProtoTest runs an in-memory broker so the messaging API still works. That broker is a test double and registers no `Broker` capability. A skip is therefore honest: the test never passes against something that is not a real broker.

## Check yourself

<Checkpoint question="The test passes in an environment with a broker and skips in one without. Which of the two results would hide a missing event, and which would not?">

Neither hides it. With a broker, a missing event ends in a `TimeoutException` and a failure. Without one, the skip is reported with its reason, so nobody reads it as a pass. Only a test that passed against the in-memory double could hide the problem, and the capability check prevents that.

</Checkpoint>

## Remember

- Await a message with a predicate and a timeout, never with a sleep.
- Match on something unique to your test, such as the invoice id.
- No broker means no `Broker` capability, and the test skips with a reason.

Next: [Check a generated file](./workbook-as-attachment.md).

## Go deeper

- [Messaging integration](/docs/integrations/messaging): taps, declared destinations and adapters.
- [Skip conditions](/docs/foundation/skip-conditions): how a missing capability skips a test.
