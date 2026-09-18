# ProtoTest.Messaging

Messaging for ProtoTest: publish messages and await the one that matters, with a predicate and a timeout, over a broker capability.

```bash
dotnet add package ProtoTest.Messaging --prerelease
```

```csharp
builder.AddMessaging();

// Publish a command the application consumes.
await Proto.Context.Messages().PublishAsync("invoices-create", """{"id":42}""");

// Await the event the application emits; failing to arrive is a real failure.
var message = await Proto.Context.Messages().AwaitAsync(
    "invoice.paid",
    candidate => candidate.Payload!.Contains("\"id\":42"));
```

- Without an adapter the in-memory broker is used, so the API works anywhere; it registers **no** `Broker` capability, so tests that need a real broker skip through `[RequiresCapability(ProtoCapabilityKinds.Broker)]` instead of passing against the double.
- The connection is a run-scoped resource, released with the run. Each test gets its own client, scoped to a position snapshot taken at setup, so a shared deployed broker cannot leak another test's messages into this one. An adapter may prepare its taps before the test acts; with RabbitMQ the tap is one queue per awaited destination for the run, prepared (and purged) per test, so parallel tests need distinct destinations.
- Publish to the exchange named like the destination; await with a predicate on the same destination. See the [messaging guide](https://github.com/MSeys/ProtoTest/blob/main/docs/docs/integrations/messaging/index.md).
