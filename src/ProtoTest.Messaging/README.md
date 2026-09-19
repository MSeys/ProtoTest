# ProtoTest.Messaging

Publish messages and await the one that matters, with a predicate and a timeout, over a broker capability.

```bash
dotnet add package ProtoTest.Messaging
```

## Quick start

```csharp
builder.AddMessaging();

// Publish a command the application consumes.
await Proto.Context.Messaging().PublishAsync("invoices-create", """{"id":42}""");

// Await the event the application emits; failing to arrive is a real failure.
var message = await Proto.Context.Messaging().AwaitAsync(
    "invoice.paid",
    candidate => candidate.Payload!.Contains("\"id\":42"));

message.ShouldMatchShape(Proto.Context, new { id = 42, status = "Paid" });
```

## What it adds

- **Client** — `Proto.Context.Messaging()` with `PublishAsync(destination, payload?, headers?, contentType?)` and `AwaitAsync(destination, predicate, timeout?)`; without a timeout the configured default applies.
- **Brokers** — `AddMessaging(messaging => messaging.UseBroker(...))` attaches an adapter; with none, an in-memory double serves the run and registers no `Broker` capability, so `[RequiresCapability(ProtoCapabilityKinds.Broker)]` skips where no real broker exists.
- **Assertion** — `message.ShouldMatchShape(Proto.Context, shape)` records `assert.json.shape` and a `messaging.contract.shape` observation, naming the destination when the payload is empty or not JSON.
- **Capture** — `CaptureAttachments()` records redacted published and matched payloads as `message-publish-…`/`message-receive-…` attachments; a capture failure is a `messaging.attachment.failed` event and never fails the operation.
- **Tracing** — `messaging.publish`/`messaging.await` operations and `messaging.publish`/`messaging.receive` observations; the broker is a run-scoped resource.

## Configuration

| Key | Type | Default |
| --- | --- | --- |
| `ProtoTest:Messaging:DefaultTimeout` | `TimeSpan` | `00:00:10` |
| `ProtoTest:Messaging:Destinations` | `IList<string>` | empty |
| `ProtoTest:Messaging:Attachments:CapturePublishedPayloads` | `bool` | `true` |
| `ProtoTest:Messaging:Attachments:CaptureReceivedPayloads` | `bool` | `true` |
| `ProtoTest:Messaging:Attachments:RedactSensitiveData` | `bool` | `true` |
| `ProtoTest:Messaging:Attachments:MaxDiagnosticBodyLength` | `int` | `65536` |
| `ProtoTest:Messaging:Attachments:SensitiveJsonProperties` | `List<string>` | `password`, `token`, `access_token`, `refresh_token`, `secret`, `apiKey`, `api_key` |

The in-memory double only sees messages published after its consumer starts, payloads are UTF-8 strings only, and the package ships no collector, so observations reach a report only through one.

## Learn more

- [Messaging guide](https://prototest.dev/docs/integrations/messaging/)
- [MessagingJourney.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/MessagingJourney.cs)
