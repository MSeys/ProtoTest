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

The RabbitMQ connection is shared for the run, while awaited messages remain isolated per test.

## Learn more

- [Messaging integration](https://prototest.dev/docs/integrations/messaging/)
- [API publishes an event](https://prototest.dev/docs/recipes/api-publishes-an-event)
- [Messaging demo](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/MessagingJourney.cs)
