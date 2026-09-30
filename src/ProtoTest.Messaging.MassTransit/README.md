# ProtoTest.Messaging.MassTransit

> Preview: the surface can change before 1.2.

Bridges the ProtoTest messaging surface to the application's MassTransit test harness: publish a
command and await the events the application publishes, over the in-process `ITestHarness` the
application composes with `AddMassTransitTestHarness`. The package also carries the MassTransit wire
envelope (`MassTransitEnvelope`), so the same surface can test a published application through a real
broker without a harness.

```bash
dotnet add package ProtoTest.Messaging.MassTransit
```

```csharp
// The application under test (in-process):
builder.Services.AddMassTransitTestHarness(cfg => cfg.AddConsumer<PaymentReceivedConsumer>());

// The suite: the application's server first, then the messaging bridge.
builder.AddAspNetCoreServer<Program>();
builder.AddMessaging(messaging => messaging
    .Tap("InvoicePaid")            // snapshot the harness during setup, before the act publishes
    .UseMassTransit<Program>());

[ProtoTest]
public async Task PayingAnInvoicePublishesTheEvent()
{
    await Proto.Context.Messaging().PublishAsync("PaymentReceived", """{"invoiceId":42,"amount":10.5}""");

    var paid = await Proto.Context.Messaging().AwaitAsync(
        "InvoicePaid",
        message => message.Payload!.Contains("\"invoiceId\":42"));

    paid.Should.MatchShape(new { invoiceId = 42, amount = 10.5 });
}
```

A destination names a message contract type (its full name, its short name, or its
`urn:message:` URN), because that is what a MassTransit bus addresses; the payload is JSON for that
contract. The `Broker` capability is declared only while the application's `BaseUrl` is not
configured, so a published application drops it and gated tests skip.

## Published applications: the wire envelope

When the application runs as a real process with a real transport, register the RabbitMQ adapter and
speak the wire format with `MassTransitEnvelope`: the frame is MassTransit's own JSON envelope
(`application/vnd.masstransit+json`, the contract's `urn:message:` URNs, ids, sent time, headers), and
the destination is the exchange MassTransit names after the contract (`Namespace:Type`):

```csharp
builder.AddMessaging(messaging => messaging
    .Tap("Billing:InvoicePaid")
    .UseRabbitMq());

var frame = MassTransitEnvelope.Wrap("Billing:PaymentReceived", new PaymentReceived(42, 10.5m));
await Proto.Context.Messaging().PublishAsync(
    frame.Destination, frame.Payload, frame.Headers, frame.ContentType);

var published = await Proto.Context.Messaging().AwaitAsync("Billing:InvoicePaid", _ => true);
var paid = MassTransitEnvelope.Unwrap<InvoicePaid>(published);
var envelope = MassTransitEnvelope.Unwrap(published);   // URNs, ids, sent time, raw payload
```

`Wrap` takes the contract instance or a raw JSON payload plus its type; `Unwrap`/`Unwrap<T>` fail naming the destination when the frame is not a MassTransit envelope. Both conversions are pure and work with any broker adapter. Pass `MassTransitEnvelopeAddresses` when a consumer replies through `RespondAsync` and the test awaits the reply on the response address.

## Includes

- `UseMassTransit<TProgram>(application)` registration on the `AddMessaging` chain; the adapter
  publishes over the harness bus and awaits the harness's published messages.
- The full messaging test surface: `PublishAsync`/`AwaitAsync`, `ProtoMessage.ReadRequired<T>()`,
  `message.Should.MatchShape(shape)`, payload attachments, and the `messaging.*` trace vocabulary.
- `Tap` snapshots the harness during setup, so a message the act published is never missed.
- `MassTransitEnvelope.Wrap`/`Unwrap` speak the MassTransit wire envelope through any broker adapter,
  so a published application publishes what a MassTransit consumer expects and reads what the bus
  published, typed or as envelope metadata.

## Limits

- The harness bridge is in-process only: `UseMassTransit` resolves `ITestHarness` from the
  application's `AddAspNetCoreServer` server, so the application must compose
  `AddMassTransitTestHarness` and the suite must register `AddMessaging` after the server. A published
  application (configured `BaseUrl`) has no harness here and its capability is dropped. Test a published
  application through the broker with `MassTransitEnvelope` instead.
- The harness is MassTransit's in-memory test transport, which replaces the application's real
  transport in the test process. The bridge tests the application's bus behaviour, not a broker.
- `AwaitAsync` observes what the bus published; the application's consumption is not part of the
  surface. Routing keys and queue destinations do not apply: a destination is a message contract type.
- `Declare` is a no-op: MassTransit owns message topology.
- Publishing needs a concrete message contract; an interface contract can be awaited when the
  application publishes it, but not published from the test.
- There is no container package for the bus: a harness-mode suite needs no container. An envelope-mode
  suite over RabbitMQ starts `ProtoTest.Messaging.RabbitMq.Testcontainers`.

## Learn more

- [MassTransit bridge](https://prototest.dev/docs/integrations/messaging/masstransit)
- [Messaging integration](https://prototest.dev/docs/integrations/messaging/)
- [API publishes an event](https://prototest.dev/docs/recipes/api-publishes-an-event)
