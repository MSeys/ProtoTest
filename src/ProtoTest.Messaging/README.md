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

Publishing, waiting and matched payloads can be traced and captured as attachments.

## Learn more

- [Messaging integration](https://prototest.dev/docs/integrations/messaging/)
- [API publishes an event](https://prototest.dev/docs/recipes/api-publishes-an-event)
- [Messaging demo](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/MessagingJourney.cs)
