# ProtoTest.Messaging

Messaging for ProtoTest: publish messages and await the one that matters, with a predicate and a timeout, over a broker capability.

```bash
dotnet add package ProtoTest.Messaging --prerelease
```

```csharp
builder.AddMessaging();

// Publish a command the application consumes.
await Proto.Context.Messaging().PublishAsync("invoices-create", """{"id":42}""");

// Await the event the application emits; failing to arrive is a real failure.
var message = await Proto.Context.Messaging().AwaitAsync(
    "invoice.paid",
    candidate => candidate.Payload!.Contains("\"id\":42"));
```

- Without an adapter the in-memory broker is used, so the API works anywhere; it registers **no** `Broker` capability, so tests that need a real broker skip through `[RequiresCapability(ProtoCapabilityKinds.Broker)]` instead of passing against the double.
- `message.ShouldMatchShape(Proto.Context, shape)` matches a JSON payload with the same matcher as REST, GraphQL and gRPC; it records an `assert.json.shape` operation with a `messaging.contract.shape` observation, and names the destination when the payload is empty or not JSON.
- `CaptureAttachments` records published payloads (after the broker call succeeded) and every payload matched by an await as `message-publish-{destination}-{sequence}-payload` and `message-receive-{destination}-{sequence}-payload` attachments; the per-test sequence keeps repeated captures distinct. Sensitive JSON properties are redacted — in the trace's `Message` section too — and payloads are capped at `MaxDiagnosticBodyLength` through the `ProtoTest:Messaging:Attachments` section (64 KiB, `RedactSensitiveData`); a non-JSON payload is captured as `text/plain`. Set `CapturePublishedPayloads` or `CaptureReceivedPayloads` to `false` to disable either side. Capture never fails the operation: a failed capture is a `messaging.attachment.failed` trace event and the publish or await still succeeds.
- The connection is a run-scoped resource, released with the run. Each test gets its own consumer, created at setup and disposed with the test: an adapter may prepare the destinations it will await before the test acts, and removes them at the test's end. With RabbitMQ that is one exclusive queue per test per destination, so parallel tests on the same destination cannot steal each other's messages.
- Publish to the exchange named like the destination; await with a predicate on the same destination. See the [messaging guide](https://github.com/MSeys/ProtoTest/blob/main/docs/docs/integrations/messaging/index.md).
