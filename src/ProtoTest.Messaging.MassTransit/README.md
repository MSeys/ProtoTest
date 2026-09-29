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

`Wrap` takes the contract instance or a raw JSON payload plus its type; `Unwrap`/`Unwrap<T>` fail with
`MessagingAssertionException` naming the destination when the frame is not a MassTransit envelope, or
when its `messageType` does not declare `T`. Both conversions are pure and work with any
`IProtoMessageBroker`; the in-memory broker round-trips a frame too. The JSON envelope path needs no
`MT-*` transport headers; the caller's headers ride the frame and the envelope's `headers` object.
Pass `MassTransitEnvelopeAddresses` to set the request/response fields: a consumer that replies through
`RespondAsync` reads `responseAddress` and `requestId` from the frame, and `Unwrap` returns the
addresses and request id beside the ids it already reads. Awaiting the reply is the test's own broker
await on the response address.

## Includes

- `UseMassTransit<TProgram>(application)` registration on the `AddMessaging` chain; the adapter
  publishes over the harness bus and awaits the harness's published messages.
- The application's own `IPublishEndpoint` publishes are what a test awaits, so an act-then-await
  journey needs no broker client of its own.
- The full messaging test surface: `PublishAsync`/`AwaitAsync`, `ProtoMessage.ReadRequired<T>()`,
  `message.Should.MatchShape(shape)`, payload attachments, and the `messaging.publish`,
  `messaging.published`, `messaging.receive`, `messaging.contract.shape` and `messaging.failure`
  trace vocabulary (`messaging.system` is `MassTransit`).
- Headers ride the publish context; the awaited message carries the headers the message has.
- Routing keys do not apply: a destination is a message contract type, so `PublishAsync`/`AwaitAsync`
  with one fail with an error naming the destination instead of dropping it.
- `Tap` snapshots the harness during setup, so a message the act published is never missed.
- `MassTransitEnvelope.Wrap`/`Unwrap` speak the MassTransit wire envelope through any broker adapter,
  so a published application - or any suite without a harness - publishes what a MassTransit consumer
  expects and reads what the bus published, typed or as envelope metadata.

## Limits

- The harness bridge is in-process only: `UseMassTransit` resolves `ITestHarness` from the
  application's `AddAspNetCoreServer` server, so the application must compose
  `AddMassTransitTestHarness` and the suite must register `AddMessaging` after the server. A published
  application (configured `BaseUrl`) has no harness here and its capability is dropped; a loopback,
  container or Aspire application is a different process boundary and is not served. Test a published
  application through the broker with `MassTransitEnvelope` instead.
- There is no container package for the bus: MassTransit is a bus library, not a server, so a
  harness-mode suite needs no container. An envelope-mode suite over RabbitMQ starts
  `ProtoTest.Messaging.RabbitMq.Testcontainers` - the container belongs to the broker's package, not
  to this one.
- The harness is MassTransit's in-memory test transport, which replaces the application's real
  transport in the test process. The bridge tests the application's bus behaviour, not a broker.
- `MassTransitEnvelope` converts the frame; it does not resolve a broker address. Pass the destination
  the bus publishes to - on RabbitMQ the exchange named by its entity name formatter
  (`Namespace:Type`) - and `Tap` or `Declare` it like any other destination. `Wrap` writes the
  request/response fields only when the caller passes `MassTransitEnvelopeAddresses`; awaiting the
  reply is the suite's own broker await on the response address.
- A queue destination is refused: a MassTransit destination is a message contract type, so
  `AwaitAsync("queue:…")` fails naming the contract type to await instead. Use the RabbitMQ adapter to
  read a queue.
- `Declare` is a no-op: MassTransit owns message topology, and every message contract already exists
  on the bus or is created on first use.
- `AwaitAsync` observes what the bus **published**; the application's consumption is not part of the
  surface.
- The payload is the contract re-serialized with the shared web JSON defaults. A null or empty payload publishes a default instance; a contract with no parameterless constructor fails the publish with an error naming the contract.
- Publishing needs a concrete message contract; an interface contract can be awaited when the
  application publishes it, but not published from the test.
- The harness keeps its published history for the whole run, and each test's consumer snapshots the
  position at setup (`Tap`) or at its first await. A test that substitutes the application
  (`Override`/`[ReplaceService]`) is served by its own dedicated server, whose fresh harness the
  consumer re-baselines to. Use test-owned destinations in parallel suites.
- One broker per run and one application per bridge: the first adapter registered wins, and the
  harness is the one of the named application.

## Learn more

- [MassTransit bridge](https://prototest.dev/docs/integrations/messaging/masstransit)
- [Messaging integration](https://prototest.dev/docs/integrations/messaging/)
- [API publishes an event](https://prototest.dev/docs/recipes/api-publishes-an-event)
