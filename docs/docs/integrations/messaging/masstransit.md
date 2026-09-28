---
sidebar_position: 11
title: MassTransit bridge
sidebar_label: MassTransit
description: "Bridge the messaging surface to the application's in-process MassTransit test harness: publish commands and await the events the application publishes."
---

# MassTransit bridge

`ProtoTest.Messaging.MassTransit` makes the application's MassTransit bus the broker the [messaging surface](./index.md) talks to. The application composes the MassTransit **test harness** (`AddMassTransitTestHarness`), the suite hosts the application in-process with `AddAspNetCoreServer`, and the bridge resolves the harness from that server. The test then publishes and awaits with the same `context.Messaging()` API as every other broker, and the application's own `IPublishEndpoint` publishes are the messages a test awaits.

The package also carries the **wire envelope** (`MassTransitEnvelope`), so a suite can test a MassTransit application that runs as a real process behind a real broker - or publish what a MassTransit consumer expects through any broker adapter - without a harness at all. See [Envelope interop](#envelope-interop).

```bash
dotnet add package ProtoTest.Messaging.MassTransit
```

The package targets **.NET 8, 9 and 10** and pins MassTransit 8.5.10 (Apache-2.0).

## The application side

The application under test registers its consumers and the test harness, exactly as it would for a MassTransit test suite:

```csharp
// Program.cs
builder.Services.AddMassTransitTestHarness(cfg => cfg.AddConsumer<PaymentReceivedConsumer>());
```

The harness is MassTransit's in-memory test transport: it replaces the transport the application would use in production, so the bus, the consumers and the topology under test are the application's own. A deployed environment that composes a real transport instead has no `ITestHarness`, which is what the capability rule below is about.

## Registering

```csharp
builder
    .AddAspNetCoreServer<Program>()                       // the in-process application, first
    .AddMessaging(messaging => messaging
        .Tap("InvoicePaid")                               // snapshot the harness during setup
        .UseMassTransit<Program>());                      // the bridge, after the server
```

```csharp
ProtoMessagingBuilder UseMassTransit<TProgram>(this ProtoMessagingBuilder messaging,
    string application = "Default");
```

`TProgram` is the application's entry point class; `application` names the `AddAspNetCoreServer` registration whose harness serves (default `Default`). Register `AddMessaging` **after** the application's server: the messaging client initializes once the server exists, the same ordering the in-process RabbitMQ topology needs. The bridge is the only adapter for the run - a repeated `AddMessaging` keeps the first adapter configured, and one run has one broker.

## Destinations

A destination names a **message contract type** - the addressing unit of a MassTransit bus:

```csharp
await Proto.Context.Messaging().AwaitAsync("InvoicePaid", message => message.Payload!.Contains("42"));
await Proto.Context.Messaging().AwaitAsync("Billing.InvoicePaid", message => ...);       // full name
await Proto.Context.Messaging().AwaitAsync("urn:message:Billing:InvoicePaid", message => ...);
```

The short name, the full name and the `urn:message:` URN all name the same contract; a publish, and a tap during setup, resolve the name before it becomes a bus address, so a destination that names no loaded contract - or a short name that two loaded contracts share - fails naming it (or the candidates). An await matches a published message by the name as written, so an untapped ambiguous short name matches either contract's messages and an untapped unknown one times out. The awaited `ProtoMessage.Destination` is the contract's full name.

## Publishing

`PublishAsync(destination, payload)` resolves the destination to its contract type, deserializes the JSON payload into it with the shared web JSON defaults, and publishes the instance over the harness bus, so the application's consumers receive it:

```csharp
await Proto.Context.Messaging().PublishAsync(
    "PaymentReceived",
    """{"invoiceId":42,"amount":10.5}""");
```

Headers passed to `PublishAsync` ride the publish context and are visible on the awaited message. A null or empty payload publishes the contract's default instance; a contract that has none fails the publish naming it, and the fix is the contract's shape or an explicit payload. The `contentType` argument is ignored: the envelope's content type is MassTransit's (`application/vnd.masstransit+json`), and the payload the bridge produces for an await is the contract instance re-serialized as JSON.

## Awaiting

`AwaitAsync` observes what the bus **published**, including the application's own publishes, so the usual journey is act-then-await with no broker client in the suite:

```csharp
using var response = await Proto.Context.Rest().PostAsync("/invoices/42/pay");

var paid = await Proto.Context.Messaging().AwaitAsync(
    "InvoicePaid",
    message => message.Payload!.Contains("\"invoiceId\":42"));

paid.Should.MatchShape(new { invoiceId = 42, amount = 10.5m });
```

The harness keeps its published history for the whole run, so the per-test isolation is a position snapshot: each test's consumer records the harness's published count when it prepares. `Tap` (or `ProtoTest:Messaging:Destinations`) declares the destinations a test awaits, so the snapshot happens during setup, before the act publishes; a destination first reached at its await can only see later messages, exactly like a just-in-time tap on RabbitMQ. A test that substitutes the application (`Override` or `[ReplaceService]`) is served by its own dedicated server, and with it a fresh harness whose history starts empty: the consumer re-baselines to that harness, so a tapped act-then-await still sees the messages the dedicated harness published. Awaits on one consumer serialize in call order, and a message that matched no awaited predicate stays for a later await.

`Declare` is a no-op here: MassTransit owns message topology, and every message contract already exists on the bus or is created by it on first use - the same rule the in-memory broker follows.

## Envelope interop

`UseMassTransit` needs the application in-process. A **published** application - the same MassTransit code, running as a real process with its real transport - is tested against the broker itself: register the [RabbitMQ adapter](./index.md#going-further) and use `MassTransitEnvelope` to speak the wire format the bus expects.

```csharp
builder.AddMessaging(messaging => messaging
    .Tap("Billing:InvoicePaid")      // the exchange MassTransit names after the contract
    .UseRabbitMq());                 // or any other adapter - the frame is adapter-agnostic

// Publish the frame the application's consumers receive:
var frame = MassTransitEnvelope.Wrap(
    "Billing:PaymentReceived",
    new PaymentReceived(42, 10.5m),
    correlationId: invoiceId,
    headers: new Dictionary<string, string?> { ["tenant"] = "northstar" });
await Proto.Context.Messaging().PublishAsync(
    frame.Destination, frame.Payload, frame.Headers, frame.ContentType);

// Read the event the application published:
var published = await Proto.Context.Messaging().AwaitAsync("Billing:InvoicePaid", _ => true);
var paid = MassTransitEnvelope.Unwrap<InvoicePaid>(published);
var envelope = MassTransitEnvelope.Unwrap(published);   // URNs, ids, sent time, raw payload
```

```csharp
ProtoMessage    MassTransitEnvelope.Wrap<T>(string destination, T message,
    Guid? messageId = null, Guid? correlationId = null, Guid? conversationId = null,
    IReadOnlyDictionary<string, string?>? headers = null);

ProtoMessage    MassTransitEnvelope.Wrap(string destination, string payload, Type messageType,
    Guid? messageId = null, Guid? correlationId = null, Guid? conversationId = null,
    IReadOnlyDictionary<string, string?>? headers = null);

// The request/response form: responseAddress and requestId are what a RespondAsync consumer reads.
ProtoMessage    MassTransitEnvelope.Wrap<T>(string destination, T message,
    MassTransitEnvelopeAddresses addresses, Guid? messageId = null, …);
ProtoMessage    MassTransitEnvelope.Wrap(string destination, string payload, Type messageType,
    MassTransitEnvelopeAddresses addresses, Guid? messageId = null, …);

public sealed record MassTransitEnvelopeAddresses(
    Uri? SourceAddress = null, Uri? DestinationAddress = null,
    Uri? ResponseAddress = null, Guid? RequestId = null);

MassTransitEnvelopeContent MassTransitEnvelope.Unwrap(ProtoMessage message);
T                         MassTransitEnvelope.Unwrap<T>(ProtoMessage message);
```

The frame is MassTransit's own envelope, not a lookalike: the package's `JsonMessageEnvelope` serialized with the package's own serializer options, so the camelCase property set, the decimal-as-string rule, the host block and every optional field are MassTransit's, not a reconstruction. It carries the content type `application/vnd.masstransit+json` and, in `messageType`, the `urn:message:` URNs of the contract - interfaces and base message types included, exactly as the bus computes them - so a MassTransit consumer's type filter matches it. No `MT-*` transport headers are involved on this path; the envelope body carries the metadata (the `MT-*` headers belong to MassTransit's raw serializer). The caller's headers ride both the broker frame and the envelope's `headers` object, as they do on a bus publish.

`Unwrap<T>` reads like the consumer would: the envelope must declare `T` (or one of its message URNs) in `messageType`, and the `message` is deserialized with MassTransit's options. `Unwrap` returns the envelope metadata instead - `MessageTypes`, `MessageId`, `CorrelationId`, `ConversationId`, `SentTime` and the raw `Payload` - for a suite that asserts on the envelope itself. A frame that is not a MassTransit envelope (an empty payload, a plain JSON body, a missing `message` or `messageType`) throws `MessagingAssertionException` naming the destination and the reason, also for a typed unwrap whose envelope does not declare `T`. `Wrap` overloads take either the contract instance or the already-serialized JSON payload plus its type.

Both conversions are pure and need no harness, bus or server: they work with any `IProtoMessageBroker`, and the in-memory broker round-trips a wrapped frame like any other payload. The destination is the broker's address for the contract - on RabbitMQ the exchange MassTransit names after it (`Namespace:Type`, the entity name formatter's output) - because the helper does not guess topology: the test publishes to the exchange the application's bus declared, exactly like any other address.

`Wrap` writes the request/response fields when the caller gives them: `MassTransitEnvelopeAddresses` carries the source, destination and response addresses and the request id, and the frame's envelope carries them under MassTransit's own property names, so a consumer that replies through `RespondAsync` finds the response address and request id it needs. `Unwrap` returns them on `MassTransitEnvelopeContent` (`SourceAddress`, `DestinationAddress`, `ResponseAddress`, `RequestId`), null when the frame carries none - a bus-produced frame sets the source and destination addresses it was sent with. Awaiting the reply itself stays the test's own broker await on the response address: the helper converts frames, it does not run a request client.

## Skip

The harness exists only while the application is hosted in-process, so the bridge declares the `Broker` capability only while `ProtoTest:Applications:{application}:BaseUrl` is **not** configured:

```csharp
[RequiresCapability(
    ProtoCapabilityKinds.Broker,
    Reason = "The application is not hosted in-process; the MassTransit test harness is unavailable.")]
```

A published application (a configured `BaseUrl`) drops the capability and its tests skip instead of failing at setup. A suite that composes the bridge without an in-process server, or whose application does not register the harness, fails at first use with an error naming `AddAspNetCoreServer` or `AddMassTransitTestHarness` respectively - the composition promised something the environment cannot serve.

## Tracing

The bridge records the messaging vocabulary unchanged: `messaging.publish` and `messaging.await` operations with `messaging.system` set to `MassTransit`, the `messaging.published`, `messaging.receive`, `messaging.contract.shape` and `messaging.failure` observations, and the run-scoped `messaging:broker` resource. Payload sections and attachments use the shared JSON redaction.

## Limits

- **No container package for the bus.** MassTransit is a bus library, not a server, so there is nothing for Testcontainers to own: a harness-mode suite needs no container, and an envelope-mode suite over RabbitMQ starts `ProtoTest.Messaging.RabbitMq.Testcontainers` ([Owning a broker](./index.md#owning-a-broker)) and registers it like any other infrastructure. A MassTransit application's real transport is still a broker - the container belongs to that broker's package, not to this one.
- **The harness bridge is in-process only.** `UseMassTransit<TProgram>` resolves `ITestHarness` from the application's `AddAspNetCoreServer` server. A published application (`BaseUrl` configured) drops the capability; a loopback (`AddLoopbackApplication`), container (`ApplicationContainer`) or Aspire application is a different process boundary and is not served, so gate tests that need the bridge. A published application is tested through the broker instead - [Envelope interop](#envelope-interop) with the RabbitMQ adapter - not through the harness.
- **The harness's transport is the application's bus in the test process.** `AddMassTransitTestHarness` replaces the real transport with the in-memory test transport; the bridge tests the application's bus behaviour, not a broker.
- **`Wrap` writes command/event envelopes unless the caller passes the request/response fields.** Without `MassTransitEnvelopeAddresses` the source, destination and response addresses and the request id stay unset; with it, a consumer that replies through `RespondAsync` finds them, and `Unwrap` returns them on `MassTransitEnvelopeContent`. Awaiting the reply is the test's own broker await on the response address - the helper converts frames, it runs no request client. The fault, initiator and expiration fields are not exposed - add them by hand if a scenario needs them.
- **A queue destination is refused.** A MassTransit destination is a message contract type, and the bus owns its transport's topology, so `AwaitAsync("queue:…")` fails naming the destination and the contract-type address to await instead; a queue is consumed only through a broker adapter that owns queues (the RabbitMQ adapter).
- **Envelope interop is address-level.** `MassTransitEnvelope` converts the frame; it does not resolve a broker address from a contract type. Pass the destination the bus actually publishes to - on RabbitMQ the exchange named by its entity name formatter (`Namespace:Type`) - and declare or `Tap` it like any other destination. A wrapped frame carries no routing key: a MassTransit exchange is a fanout, so its routing key does not address anything.
- **No routing keys.** A MassTransit destination is a message contract type, and the bus owns its transport's routing. `PublishAsync`/`AwaitAsync` with a routing key fail with an `InvalidOperationException` naming the destination instead of dropping the key, and an awaited bus message carries no `RoutingKey`. Use the RabbitMQ adapter when a test must address an `(exchange, routingKey)` pair.
- **A raw JSON-`null` payload wraps an envelope without a message.** `Wrap(destination, "null", type)` produces an envelope whose `message` is JSON `null`, and `Unwrap` reports it as an envelope that carries no `message` - exactly like a bus frame that carried none. Give the contract's payload when the message must exist.
- **`Declare` is a no-op.** MassTransit manages message topology itself; declaring a destination neither creates nor verifies anything.
- **Awaits observe publishes, not consumption.** The application's handling of a message is visible in MassTransit's own harness, not in the ProtoTest await surface.
- **The payload is re-serialized.** The harness keeps the contract instance, not the wire bytes; the bridge serializes it with the shared web JSON defaults (camelCase), and `contentType` on a publish is ignored.
- **An empty payload needs a default instance.** A null or empty payload publishes the contract's default instance; a positional record has none, so it fails the publish naming the contract - give it a parameterless shape or publish an explicit payload.
- **Publishing needs a concrete contract.** An interface contract can be awaited when the application publishes it, but the test cannot publish one: MassTransit publishes an instance.
- **The published history is run-wide.** A parallel suite should use test-owned destinations (for example `context.UniqueName`-derived payload values) so one test's messages cannot satisfy another's await.
- **One application and one broker per run.** The first adapter registered wins; a suite that needs two buses registers two runs.

## Learn more

- [Messaging](./index.md) - the surface, destinations, attachments and the adapter contract.
- [Skip conditions](../../foundation/skip-conditions.md) - capability gates and suite-level reasons.
- [ProtoTrace](../../observability/prototrace.md) - how operations and observations land in the trace.
