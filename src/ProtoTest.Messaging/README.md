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

Declare the destinations a test awaits in code with `Tap`, so the adapter binds its tap before the act publishes:

```csharp
builder.AddMessaging(messaging => messaging
    .CaptureAttachments()
    .Tap("invoice-paid")
    .UseRabbitMq());
```

Repeated calls compose; `ProtoTest:Messaging:Destinations` still binds over the code values.

Publishing, waiting and matched payloads can be traced and captured as attachments.

## Learn more

- [Messaging integration](https://prototest.dev/docs/integrations/messaging/)
- [API publishes an event](https://prototest.dev/docs/recipes/api-publishes-an-event)
- [Messaging demo](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/MessagingJourney.cs)
