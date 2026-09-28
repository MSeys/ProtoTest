# ProtoTest.Messaging

Publish messages and await events through a shared messaging API.

```bash
dotnet add package ProtoTest.Messaging
```

```csharp
await Proto.Context.Messaging()
    .PublishAsync("invoices-create", """{"id":42}""");

var paid = await Proto.Context.Messaging()
    .AwaitAsync("invoice-paid", message => message.Payload.Contains("42"));
```

Without a broker adapter the package uses an in-memory implementation. Add `ProtoTest.Messaging.RabbitMq` when the test must communicate with RabbitMQ.

A consumed message reads typed: `message.ReadRequired<T>()` (or `ReadAsJson<T>()` for a nullable read) uses the shared JSON defaults, and `ReadRequired<T>("$.id")` reads one path.

A publish and an await can name a routing key: `PublishAsync(exchange, routingKey, payload)` addresses an `(exchange, routingKey)` pair, `AwaitAsync(exchange, routingKey, predicate)` matches only that key, and a consumed message carries what it was delivered under in `message.RoutingKey`. The RabbitMQ adapter sends and binds the key; the in-memory broker keeps and filters it.

An await can name a queue directly: `AwaitAsync(ProtoDestination.Queue("billing.session-ended.dlq"), predicate)` consumes the named queue instead of a test-owned tap, which is what a dead-letter queue needs. A queue destination reads the queue's backlog, leaves the queue as it is, and is consumed, never declared — `Tap` accepts one, `Declare` refuses the form. A broker whose model has no queues (the in-memory broker, the MassTransit bridge) refuses it with an error naming the transport.

Declare the destinations a test awaits in code with `Tap`, and the destinations the suite owns — the ones it publishes to itself — with `Declare`, so the adapter binds its taps and creates the suite's topology before the act publishes:

```csharp
builder.AddMessaging(messaging => messaging
    .CaptureAttachments()
    .Declare("invoice-paid")
    .Tap("invoice-paid")
    .UseRabbitMq());
```

Repeated calls compose; `ProtoTest:Messaging:Destinations` and `ProtoTest:Messaging:DeclaredDestinations` still bind over the code values. `Declare` is idempotent — the destination is created once per run during setup — and the in-memory broker treats it as a no-op, because every destination already exists there.

Publishing, waiting and matched payloads can be traced and captured as attachments.

## Learn more

- [Messaging integration](https://prototest.dev/docs/integrations/messaging/)
- [API publishes an event](https://prototest.dev/docs/recipes/api-publishes-an-event)
- [Messaging demo](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/MessagingJourney.cs)
