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
    'A running Docker engine for the container-backed run in step 5',
  ]}
/>

## The problem

When a user pays an invoice, the application publishes an event for other services. The API response does not show it. A test that calls `Thread.Sleep` and then looks is slow when the message is early and wrong when it is late.

## Do it

### 1. Run the test alone

From the repository root, run the existing test in `samples/Northstar.ProtoTest/BrokerJourney.cs`:

```bash
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~PayingAnInvoicePublishesAnInvoicePaidEvent"
```

With the default sample settings, the result is one skipped test. Step 5 enables RabbitMQ so the test body below can run.



### 2. Trigger the event

```csharp
var invoice = await Proto.Context.Data().IssueInvoiceAsync();

using var paid = await Proto.Context.Rest()
    .Body(new PayInvoiceRequest(PaymentMethods.Visa))
    .PostAsync("/api/v1/invoices/{invoiceId}/pay", new { invoiceId = invoice.Id });
paid.Should.HaveHttpStatus(HttpStatusCode.OK);
```

The test issues an invoice with `Proto.Context.Data()`, which creates test data for it, then pays the invoice over REST. With RabbitMQ configured, the payment endpoint publishes an `invoice.paid` event before returning HTTP 200.

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

`Proto.Context.Messaging()` returns the messaging client. `AwaitAsync` takes the destination, a predicate and a timeout. It returns the first available, unconsumed message on `invoice.paid` for which the predicate is true.

The sample searches the payload text for the invoice id. This is a substring check: an expected id of `12` could also match `123`. For strict correlation, parse the JSON and compare the complete id value.

If no matching message arrives within 15 seconds, the call throws a `TimeoutException` and the test fails. A failed broker connection can fail it earlier.

### 4. Assert the message

```csharp
using (Assert.EnterMultipleScope())
{
    Assert.That(message.ContentType, Is.EqualTo("application/json"));
    Assert.That(message.Payload, Does.Contain($"\"status\":\"{InvoiceStatuses.Paid}\""));
}
```

These NUnit assertions check the content type and the literal paid-status text. They do not validate the entire JSON payload or prove that another service consumed the event.

### 5. See what happens without a broker

The class carries `[RequiresCapability(ProtoCapabilityKinds.Broker)]`. The sample selects RabbitMQ when you set `ProtoTest:Messaging:RabbitMq:ConnectionString` or request a broker container. Without either setting, the capability is absent and the test skips before its body runs.

The setup class supplies this reason: "No broker is configured; set ProtoTest:Messaging:Broker=container." A configured address enables the capability, but does not prove the broker is reachable.

To let the run start RabbitMQ in a container, start Docker and keep the default local sample settings. In PowerShell, set the environment variable corresponding to that configuration key:

```powershell
$env:ProtoTest__Messaging__Broker = "container"
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~PayingAnInvoicePublishesAnInvoicePaidEvent"
```

Without an existing broker address, the run starts a RabbitMQ container. It supplies the address to both the test adapter and the hosted application. A configured address takes precedence over starting a container. Container startup and connection failures are errors, not missing-capability skips.

Afterwards, remove the variable, or later runs in this shell also request a broker.

In a successful run's trace, find `Messaging · await invoice.paid` under Execution and inspect its received-payload attachment. This is the test receiving the application's event. A skipped run has no payment or message await to inspect.

## What happened

The setup class calls `.Declare("invoice.paid")` to declare the destination and `.Tap("invoice.paid")` to prepare a listener before the test acts. RabbitMQ gives each test's tap its own queue. Messages can arrive there before `AwaitAsync` starts, so the test avoids subscribing only after the payment.

Preparing the listener removes that timing race. It does not guarantee delivery through broker failures or make an imprecise predicate safe.

Without an adapter, ProtoTest supplies an in-memory messaging double with no `Broker` capability. The sample application also uses a no-op event publisher when its broker address is absent. This journey's capability requirement prevents those defaults from producing a broker-test pass.

## Check yourself

<Checkpoint question="What does a missing-broker skip tell you, compared with a successful message await?">

A skip says the required broker capability was unavailable. It provides no evidence that the application published an event.

A successful await says the test received a message satisfying its predicate. The following assertions check its content type and status text. How precisely the test identifies the intended event depends on that predicate.

</Checkpoint>

## Remember

- Await a message with a predicate and a timeout, never with a sleep.
- Compare a complete correlation value, such as the invoice id, rather than a text prefix.
- A missing broker capability skips this test. A configured but broken broker can fail it.

Next: [Check a generated file](./workbook-as-attachment.md).

## Go deeper

- [Messaging integration](/docs/integrations/messaging): taps, declared destinations and adapters.
- [Skip conditions](/docs/foundation/skip-conditions): how a missing capability skips a test.
