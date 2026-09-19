---
sidebar_position: 2
title: An API call publishes an event
description: Pay an invoice over REST, then await the invoice.paid event the application publishes on RabbitMQ — with the broker started and owned by the run.
---

# An API call publishes an event

Paying an invoice should publish `invoice.paid`. The test pays over the API and then waits for that event — not with a sleep, but with a predicate and a timeout.

## Compose

```csharp
using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.Messaging;
using ProtoTest.Messaging.RabbitMq;
using ProtoTest.Messaging.RabbitMq.Testcontainers;
using ProtoTest.Rest;

protected override void Configure(IProtoHostBuilder builder) =>
    builder
        // One broker for the run, its address handed to the tests and to the application.
        .AddInfrastructure(
            RabbitMqBroker.Container(),
            RabbitMqOptions.ConnectionStringSetting,
            "Messaging:RabbitMq:ConnectionString")
        .AddApplication("Api", app => app
            .AddAspNetCoreServer<Program>()
            .AddRest(rest => rest.AddClient("Api")))
        // Last, so the application has declared its exchanges before the test's taps bind to them.
        .AddMessaging(messaging => messaging.UseRabbitMq());
```

Declare the destinations the suite awaits, so the adapter binds its tap before the test acts:

```json
{
  "ProtoTest": {
    "Messaging": {
      "Destinations": [ "invoice.paid" ]
    }
  }
}
```

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
            .ShouldHaveHttpStatus(HttpStatusCode.Created)
            .ReadAsJson<InvoiceResponse>()!;

        using var paid = await Proto.Context.Rest()
            .Body(new { method = "visa" })
            .PostAsync("/api/invoices/{invoiceId}/pay", new { invoiceId = invoice.Id });
        paid.ShouldHaveHttpStatus(HttpStatusCode.OK);

        var message = await Proto.Context.Messaging().AwaitAsync(
            "invoice.paid",
            candidate => candidate.Payload?.Contains($"\"id\":{invoice.Id}") == true,
            TimeSpan.FromSeconds(15));

        Assert.That(message.ContentType, Is.EqualTo("application/json"));
    }

    private sealed record InvoiceResponse(int Id);
}
```

## Watch for

- **Match on something this test owns.** Parallel tests pay invoices too. A predicate on the invoice id awaits *this* test's event, not the first `invoice.paid` that happens to arrive.
- **Declare before you await.** An undeclared destination is bound when `AwaitAsync` is called, and an event published before that is missed. See [Destinations](../integrations/messaging/index.md#destinations).
- **Where there is no broker, skip.** `[RequiresCapability(ProtoCapabilityKinds.Broker)]` skips the test in an environment without one, instead of passing against the in-memory double. See [Skip conditions](../foundation/skip-conditions.md).

In the trace, the payment request and the awaited message sit in the same story; a timeout names the destination and how long it waited.
