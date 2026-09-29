---
sidebar_position: 2
title: An API call publishes an event
description: Pay an invoice over REST, then await the invoice.paid event the application publishes, with the broker started and owned by the run.
---

# An API call publishes an event

## The situation

Paying an invoice should publish `invoice.paid`. A `200` on the pay call only says the payment was accepted. It does not say the application told the rest of the system.

The test pays over the API and then waits for the event with a predicate and a timeout. That ties the write side to the broker, so the assertion can only pass when the application actually published. The demo runs this journey in [BrokerJourney.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/Northstar.ProtoTest/BrokerJourney.cs).

## The code

### Compose

The run owns a RabbitMQ broker and hands its address to the application and to the tests. The demo starts one when the environment asks for it, so the piece is registered behind the selection:

```csharp
// Setup.cs: the broker the run owns, behind the configured address.
if (run.OwnsMessagingBroker)
{
    builder.AddInfrastructure(
        "MessagingBroker",
        chain => chain
            .UseConfigured()
            .UseContainer(RabbitMqBroker.Container()),
        RabbitMqOptions.ConnectionStringSetting,
        "Messaging:RabbitMq:ConnectionString");
}
```

Messaging is registered last, after the application, and declares the destination in code. `Declare` creates it during test setup and `Tap` binds it there too, so an event published before the test's first await is still received:

```csharp
// Setup.cs: registered last, so the application has declared its exchanges before the tap binds.
builder.AddMessaging(messaging => messaging
    .CaptureAttachments()
    .UseRabbitMq()
    .Declare("invoice.paid")
    .Tap("invoice.paid"));
```

### The test

The demo test pays an invoice it provisioned through [Data](../integrations/data/index.md), then awaits the event:

```csharp
[Application(NorthstarTargets.Api)]
[NorthstarMember(PlanIds.Growth)]
[RequiresCapability(ProtoCapabilityKinds.Broker)]
public sealed class BrokerJourney
{
    [ProtoTest]
    [SignedInAs]
    public async Task PayingAnInvoicePublishesAnInvoicePaidEvent()
    {
        var invoice = await Proto.Context.Data().IssueInvoiceAsync();

        using var paid = await Proto.Context.Rest()
            .Body(new PayInvoiceRequest(PaymentMethods.Visa))
            .PostAsync("/api/v1/invoices/{invoiceId}/pay", new { invoiceId = invoice.Id });
        paid.Should.HaveHttpStatus(HttpStatusCode.OK);

        var message = await Proto.Context.Messaging().AwaitAsync(
            "invoice.paid",
            candidate => candidate.Payload is not null
                && candidate.Payload.Contains(
                    $"\"id\":{invoice.Id.ToString(CultureInfo.InvariantCulture)}",
                    StringComparison.Ordinal),
            TimeSpan.FromSeconds(15));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(message.ContentType, Is.EqualTo("application/json"));
            Assert.That(message.Payload, Does.Contain($"\"status\":\"{InvoiceStatuses.Paid}\""));
        }
    }
}
```

`[NorthstarMember]` is the demo's own composite attribute: it groups an isolated tenant provisioned through [Data](../integrations/data/index.md) with the authenticator that carries the member's token through REST. `IssueInvoiceAsync` is a shortcut over the same data surface.

## What the trace shows

The trace reads as the story in order:

- the provisioning requests the data fixture made for this test,
- the REST `http.request` for the pay call, with `assert.http.status` and the response body as an `http.response` observation,
- the `messaging.await` operation with the destination `invoice.paid` and its timeout, and the matched delivery recorded as a `messaging.receive` observation with the payload section.

When the wait times out, the operation fails naming the destination and the timeout; the REST call that should have caused the publish is still in the trace. Where no broker is configured, the `Broker` capability is dropped with its reason in the run's capability evidence, and the gated test never starts.

## Variations

- **The in-memory broker.** Leave `UseRabbitMq()` off and the client runs against the run's in-memory broker. The same await works; nothing crosses a process, so it proves the publication path, not the transport.
- **MassTransit.** An application that composes its own MassTransit test harness is awaited through `UseMassTransit<Program>()`. Destinations name message contracts, `Declare` is a no-op, and the bridge needs the application in-process. See [MassTransit](../integrations/messaging/masstransit.md).
- **A queue instead of an exchange.** `ProtoDestination.Queue("queue:invoice.paid")` awaits the product's own queue, with its dead-letter bindings, on an adapter that owns queues. See [Queue destinations](../integrations/messaging/index.md#queue-destinations).
- **A configured address.** `UseConfigured()` comes first, so an environment that sets the connection string skips the container and the same run talks to that broker.

## What it does not prove

- **The await proves arrival, not delivery guarantees.** One matching message on this test's tap says nothing about duplicates, ordering or broker durability.
- **Match on something this test owns.** Parallel tests pay invoices too; a predicate on the invoice id awaits this test's event, not the first `invoice.paid` that happens to arrive.
- **Declare before you await, and make sure the destination exists.** An undeclared destination is bound when `AwaitAsync` is called, and an event published before that is missed. The adapter never creates a destination nobody asked for: the application declares its topology, and a suite that owns the broker declares its own with `Declare`. See [Suite-owned topology](../integrations/messaging/index.md#suite-owned-topology).
- **Where there is no broker, skip.** `[RequiresCapability(ProtoCapabilityKinds.Broker)]` skips the test in an environment without one instead of passing against the in-memory double. See [Skip conditions](../foundation/skip-conditions.md).
