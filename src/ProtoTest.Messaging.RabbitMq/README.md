# ProtoTest.Messaging.RabbitMq

The RabbitMQ adapter for `ProtoTest.Messaging`.

```bash
dotnet add package ProtoTest.Messaging.RabbitMq
```

Register it with `UseRabbitMq()`. Messages are published to exchanges and each test receives its own temporary queue for destinations it awaits. Declare them in code with `Tap`, so the tap binds during setup and does not miss a message published earlier in the test:

```csharp
builder.AddMessaging(messaging => messaging.Tap("invoice-paid").UseRabbitMq());
```

A destination the suite owns and nothing else declares is created with `Declare`, once per run during setup and before any tap binds: a fanout, durable, non-auto-delete exchange. A suite that owns the broker and publishes its own events needs no raw broker client:

```csharp
builder.AddMessaging(messaging => messaging
    .Declare("invoice-paid")
    .Tap("invoice-paid")
    .UseRabbitMq());
```

A destination that already exists with that shape, or a repeated declaration, is a no-op.

A queue destination — `ProtoDestination.Queue("billing.session-ended.dlq")` — is consumed as it exists instead of bound to an exchange, which is how a dead-letter queue is awaited: the adapter checks the queue passively, reads it on the test's channel and leaves it in place. The queue form is consumed, never declared, and the delivery carries the queue destination and the transport's routing key. Unlike a tap, a queue consume reads the queue's backlog and removes what it reads, so a queue with another reader is shared: prefer the exchange that feeds it where the suite runs in parallel.

A publish can name a routing key with `PublishAsync(exchange, routingKey, payload)`, and an await can select one with `AwaitAsync(exchange, routingKey, predicate, …)`; the delivery carries it in `message.RoutingKey`. The key is bound on the destination's tap when the keyed await starts, so a direct exchange delivers it.

The RabbitMQ connection is shared for the run, while awaited messages remain isolated per test.

## Learn more

- [Messaging integration](https://prototest.dev/docs/integrations/messaging/)
- [API publishes an event](https://prototest.dev/docs/recipes/api-publishes-an-event)
- [Messaging demo](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/MessagingJourney.cs)
