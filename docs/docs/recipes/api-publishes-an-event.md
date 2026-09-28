---
sidebar_position: 2
title: An API call publishes an event
description: Pay an invoice over REST, then await the invoice.paid event the application publishes on RabbitMQ — with the broker started and owned by the run.
---

# An API call publishes an event

Paying an invoice should publish `invoice.paid`. The test pays over the API and then waits for that event — not with a sleep, but with a predicate and a timeout. This is what a single API assertion cannot prove: that the write side and the broker actually meet.

The same journey runs in the demo — [MessagingJourney.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/MessagingJourney.cs) (test) and [Setup.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/Setup.cs) (host). The full API surface is in [Messaging](../integrations/messaging/index.md).

## Compose

```csharp
protected override void Configure(IProtoHostBuilder builder) =>
    builder
        // One broker for the run, its address handed to the tests and to the application.
        .AddInfrastructure(
            "MessagingBroker",
            chain => chain
                .UseConfigured()
                .UseContainer(RabbitMqBroker.Container()),
            RabbitMqOptions.ConnectionStringSetting,
            "Messaging:RabbitMq:ConnectionString")
        .AddApplication("Api", app => app
            .AddAspNetCoreServer<Program>()
            .AddRest(rest => rest.AddClient("Api")))
        // Last, so the application has declared its exchanges before the test's taps bind to them.
        .AddMessaging(messaging => messaging
            // Pre-bind every destination the act publishes to; a tap declared at the first await
            // misses anything published before it.
            .Tap("invoice.paid")
            .UseRabbitMq());
```

`Tap` declares the destination in code, where the test's intent lives. The `ProtoTest:Messaging:Destinations` configuration key still applies over it, so an environment can add its own destinations.

## When the suite owns the broker

The composition above lets the application declare its exchanges at startup. A run that owns the broker itself and publishes its own events has no application initializer to declare them, so the suite declares the destinations it owns with `Declare`:

```csharp
builder
    .AddInfrastructure(
        "MessagingBroker",
        chain => chain
            .UseConfigured()
            .UseContainer(RabbitMqBroker.Container()),
        RabbitMqOptions.ConnectionStringSetting,
        "Messaging:RabbitMq:ConnectionString")
    .AddMessaging(messaging => messaging
        // The suite owns this event: create it during setup, before any tap binds.
        .Declare("invoice.paid")
        .Tap("invoice.paid")
        .UseRabbitMq());
```

`Declare` creates each destination once per run, during test setup and before the first tap binds: on RabbitMQ a fanout, durable, non-auto-delete exchange — the shape the sample application declares. It is idempotent, so a destination that already exists with that shape, or a repeated declaration, is a no-op, and a broker that refuses the declaration fails setup with the destination named. `Tap` still does the awaiting; `Declare` only makes the destination exist.

## The test

```csharp
[Application("Api")]
public sealed class InvoiceTests
{
    [ProtoTest]
    public async Task Paying_an_invoice_publishes_invoice_paid()
    {
        using var issued = await Proto.Context.Rest()
            .Body(new { customer = $"customer-{Proto.Context.TestId}", amount = 120 })
            .PostAsync("/api/invoices");
        var invoice = issued
            .Should.HaveHttpStatus(HttpStatusCode.Created)
            .ReadAsJson<InvoiceResponse>()!;

        using var paid = await Proto.Context.Rest()
            .Body(new { method = "visa" })
            .PostAsync("/api/invoices/{invoiceId}/pay", new { invoiceId = invoice.Id });
        paid.Should.HaveHttpStatus(HttpStatusCode.OK);

        var message = await Proto.Context.Messaging().AwaitAsync(
            "invoice.paid",
            candidate => candidate.Payload?.Contains($"\"id\":{invoice.Id}") == true,
            TimeSpan.FromSeconds(15));

        Assert.That(message.ContentType, Is.EqualTo("application/json"));
    }

    private sealed record InvoiceResponse(int Id);
}
```

## What it proves

The `200` says the payment was accepted; the awaited message says the application told the world. Both operations land in the same test's trace: the REST request (with its response) and the `messaging.await` that matched, in order.

## Limits

- **The await proves arrival, not delivery guarantees.** One matching message on this test's tap says nothing about duplicates, ordering or broker durability.
- **Match on something this test owns.** Parallel tests pay invoices too; a predicate on the invoice id awaits *this* test's event, not the first `invoice.paid` that happens to arrive.
- **Declare before you await, and make sure the destination exists.** An undeclared destination is bound when `AwaitAsync` is called, and an event published before that is missed; the adapter never creates a destination nobody asked for — the application declares its topology, and a suite that owns the broker declares its own with `Declare`. See [Destinations](../integrations/messaging/index.md#suite-owned-topology).
- **Where there is no broker, skip.** `[RequiresCapability(ProtoCapabilityKinds.Broker)]` skips the test in an environment without one, instead of passing against the in-memory double. See [Skip conditions](../foundation/skip-conditions.md).
