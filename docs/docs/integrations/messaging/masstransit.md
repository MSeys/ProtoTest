---
sidebar_position: 11
title: MassTransit bridge
sidebar_label: MassTransit
description: "Bridge the messaging surface to the application's in-process MassTransit test harness: publish commands and await the events the application publishes."
---

# MassTransit bridge

`ProtoTest.Messaging.MassTransit` makes the application's own MassTransit bus the broker that the
[messaging client](./index.md) talks to. A test publishes and awaits with the same `context.Messaging()` calls as
on any other broker, and awaits the events the application publishes through its own `IPublishEndpoint`.

It works in two ways:

- **Harness mode.** The application composes the MassTransit test harness (`AddMassTransitTestHarness`), and the suite hosts it in-process with `AddAspNetCoreServer`. The bridge reads and writes that harness.
- **Envelope mode.** For an application that runs as a real process behind a real broker, `MassTransitEnvelope` writes and reads the MassTransit wire format over any broker adapter. No harness is needed. See [Envelope interop](#envelope-interop).

```bash
dotnet add package ProtoTest.Messaging.MassTransit
```

The package targets **.NET 8, 9 and 10** and pins MassTransit 8.5.10 (Apache-2.0).

## The application side

The application registers its consumers and the test harness, as it would for a MassTransit test suite:

```csharp
// Program.cs
builder.Services.AddMassTransitTestHarness(cfg => cfg.AddConsumer<PaymentReceivedConsumer>());
```

The harness is MassTransit's in-memory test transport. It replaces the transport the application uses in
production, so the bus, the consumers and the topology are the application's own. A deployed environment that
uses a real transport has no `ITestHarness`. The [skip rule](#skip) covers that case.

## Registering

```csharp
builder
    .AddAspNetCoreServer<Program>("Api")                  // the in-process application, first
    .AddMessaging(messaging => messaging
        .Tap("InvoicePaid")                               // snapshot the harness during setup
        .UseMassTransit<Program>("Api"));                 // the bridge, after the server
```

```csharp
ProtoMessagingBuilder UseMassTransit<TProgram>(this ProtoMessagingBuilder messaging,
    string application = "Default");
```

`TProgram` is the application's entry point class. `application` names the `AddAspNetCoreServer` registration
whose harness the bridge uses, `Default` when omitted.

Register `AddMessaging` **after** the application's server, because the messaging client initializes once the
server exists. The bridge is the run's only adapter: one run has one broker, and a repeated `AddMessaging` keeps
the first adapter.

## Destinations

A destination names a **message contract type**, which is how a MassTransit bus addresses messages:

```csharp
await Proto.Context.Messaging().AwaitAsync("InvoicePaid", message => message.Payload!.Contains("42"));
await Proto.Context.Messaging().AwaitAsync("Billing.InvoicePaid", message => ...);       // full name
await Proto.Context.Messaging().AwaitAsync("urn:message:Billing:InvoicePaid", message => ...);
```

The short name, the full name and the `urn:message:` URN name the same contract. A publish, and a tap during setup,
resolve the name to a contract first. A name that matches no loaded contract fails with that name. A short name two
contracts share fails with both candidates.

An await without a tap matches the name as written. So an untapped, ambiguous short name can match either
contract, and an untapped unknown name times out. The awaited `ProtoMessage.Destination` is the contract's full
name.

## Publishing

`PublishAsync(destination, payload)` finds the contract type, deserializes the JSON payload into it with the shared
web JSON defaults, and publishes the instance on the harness bus. The application's consumers receive it:

```csharp
await Proto.Context.Messaging().PublishAsync(
    "PaymentReceived",
    """{"invoiceId":42,"amount":10.5}""");
```

- Headers passed to `PublishAsync` travel with the publish and are visible on the awaited message.
- A null or empty payload publishes the contract's default instance. A contract without one fails the publish, naming the contract.
- The `contentType` argument is ignored. The bus uses MassTransit's content type (`application/vnd.masstransit+json`), and an awaited payload is the contract instance serialized again as JSON.

## Awaiting

`AwaitAsync` sees what the bus **published**, the application's own publishes included. A journey acts, then
awaits, with no broker client in the suite:

```csharp
using var response = await Proto.Context.Rest().PostAsync("/invoices/42/pay");

var paid = await Proto.Context.Messaging().AwaitAsync(
    "InvoicePaid",
    message => message.MatchesShape(new { invoiceId = 42 }));

paid.Should.MatchShape(new { invoiceId = 42, amount = 10.5m });
```

The harness keeps its published history for the whole run. Each test's consumer therefore remembers where the
history stood when it prepared, and only later messages can match. Name the awaited destinations with `Tap` (or
`ProtoTest:Messaging:Destinations`), so that snapshot happens during setup, before the act publishes. A destination
first reached at its await sees only messages published after it, like a late tap on RabbitMQ.

A test that substitutes services (`Override` or `[ReplaceService]`) runs on its own dedicated server, with a fresh
harness whose history starts empty. Its consumer follows that harness, so a tapped act-then-await still sees what
the dedicated harness published.

Awaits on one consumer run in call order. A message no awaited predicate matched stays for a later await.

`Declare` does nothing here. MassTransit owns message topology, and every contract already exists on the bus or is
created on first use, as on the in-memory broker.

## Envelope interop

The harness needs the application in-process. A **published** application runs the same MassTransit code as a
real process, with its real transport. Test it through the broker: register the
[RabbitMQ adapter](./index.md#compose) and use `MassTransitEnvelope` to speak the wire format the bus expects.

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

The frame is MassTransit's own envelope, not a lookalike. `Wrap` serializes the package's `JsonMessageEnvelope`
with the package's own serializer options.

:::note[What makes it MassTransit's envelope]
The camelCase property set, the decimal-as-string rule, the host block and every optional field are MassTransit's. The frame carries the content type `application/vnd.masstransit+json`. Its `messageType` lists the contract's `urn:message:` URNs, interfaces and base types included, as the bus computes them, so a consumer's type filter matches. The envelope body carries the metadata; no `MT-*` transport headers are involved (those belong to MassTransit's raw serializer). Your headers travel on both the broker frame and the envelope's `headers` object, as on a bus publish.
:::

**Reading.** `Unwrap<T>` reads as a consumer would: the envelope must list `T` (or one of its URNs) in
`messageType`, and `message` is deserialized with MassTransit's options. `Unwrap` returns the metadata instead:
`MessageTypes`, `MessageId`, `CorrelationId`, `ConversationId`, `SentTime` and the raw `Payload`. A frame that is
not a MassTransit envelope (an empty payload, a plain JSON body, a missing `message` or `messageType`) throws
`MessagingAssertionException` naming the destination and the reason. So does a typed unwrap whose envelope does
not list `T`.

**Writing.** `Wrap` takes either the contract instance, or the serialized JSON payload plus its type. Both
conversions need no harness, bus or server. They work with any `IProtoMessageBroker`, and the in-memory broker
round-trips a wrapped frame like any payload.

**Addresses.** The destination is the broker's address for the contract. On RabbitMQ that is the exchange
MassTransit names after it (`Namespace:Type`, the entity name formatter's output). The helper does not guess
topology: publish to the exchange the application's bus declared.

**Request and response.** Pass `MassTransitEnvelopeAddresses` to set the source, destination and response
addresses and the request id, under MassTransit's own property names. A consumer that replies with `RespondAsync`
finds what it needs. `Unwrap` returns them on `MassTransitEnvelopeContent` (`SourceAddress`, `DestinationAddress`,
`ResponseAddress`, `RequestId`), or null when the frame has none. A bus-produced frame sets the source and
destination it was sent with. Awaiting the reply is your own broker await on the response address: the helper
converts frames and runs no request client.

## Skip

The harness exists only while the application runs in-process. So the bridge declares the `Broker` capability only
while the winner of that application's provider chain runs it in-process:

```csharp
[RequiresCapability(
    ProtoCapabilityKinds.Broker,
    Reason = "The application is not hosted in-process; the MassTransit test harness is unavailable.")]
```

An application served by a configured address, a loopback listener, a container or an AppHost drops the
capability, and its gated tests skip instead of failing at setup. For an application with no provider chain, a
configured `ProtoTest:Applications:{application}:BaseUrl` drops it.

A suite that composes the bridge without an in-process server fails at first use with an error naming
`AddAspNetCoreServer`. One whose application does not register the harness fails naming
`AddMassTransitTestHarness`.

## Tracing

```text
messaging.publish · PaymentReceived                  # messaging.system = MassTransit
└─ messaging.published observation (target MassTransit)

messaging.await · InvoicePaid
├─ messaging.system = MassTransit, messaging.timeout_ms = 10000
└─ messaging.receive observation on match (the bus publish the test awaited)
```

The bridge records the same messaging operations and observations as every adapter, with `messaging.system` set to
`MassTransit`: `messaging.publish` and `messaging.await`, the `messaging.published`, `messaging.receive`,
`messaging.contract.shape` and `messaging.failure` observations, and the run-scoped `messaging:broker` resource.
Payload sections and attachments use the shared JSON redaction.

## Limits

- **No container package for the bus.** MassTransit is a library, not a server. Harness mode needs no container. Envelope mode over RabbitMQ uses `ProtoTest.Messaging.RabbitMq.Testcontainers` ([Owning a broker](./index.md#owning-a-broker)), like any RabbitMQ suite.
- **The harness bridge is in-process only.** `UseMassTransit<TProgram>` reads `ITestHarness` from the application's `AddAspNetCoreServer` server. A published (`BaseUrl`), loopback (`UseLoopback`), container (`ApplicationContainer`) or Aspire application runs in another process, so the capability drops and gated tests skip. Test those through the broker with [Envelope interop](#envelope-interop).
- **It tests the bus, not a broker.** `AddMassTransitTestHarness` replaces the real transport with the in-memory test transport.
- **Some envelope fields are not exposed.** Without `MassTransitEnvelopeAddresses`, `Wrap` writes a command or event envelope with no addresses or request id. The fault, initiator and expiration fields are not exposed; add them by hand if a scenario needs them.
- **A queue destination is refused.** `AwaitAsync("queue:…")` fails, naming the destination and the contract address to await instead. Only an adapter that owns queues, such as RabbitMQ, consumes one.
- **Envelope interop works on addresses.** `MassTransitEnvelope` converts the frame but does not find a broker address for a contract type. Pass the address the bus publishes to, and `Tap` it like any destination. A wrapped frame has no routing key: a MassTransit exchange is a fanout.
- **No routing keys.** `PublishAsync` and `AwaitAsync` with a routing key fail with an `InvalidOperationException` naming the destination, instead of dropping the key. An awaited bus message has no `RoutingKey`. Use the RabbitMQ adapter for an `(exchange, routingKey)` pair.
- **A JSON `null` payload wraps an envelope without a message.** `Wrap(destination, "null", type)` writes `message` as JSON `null`, and `Unwrap` reports an envelope without a message, as for a bus frame without one.
- **`Declare` does nothing.** It neither creates nor checks a destination.
- **Awaits see publishes, not consumption.** How the application handled a message is visible in MassTransit's own harness, not through `AwaitAsync`.
- **The payload is serialized again.** The harness keeps the contract instance, not the wire bytes. The bridge serializes it with the shared web JSON defaults (camelCase), and ignores `contentType` on a publish.
- **An empty payload needs a default instance.** A positional record has none, so an empty publish fails naming the contract. Give it a parameterless shape, or publish an explicit payload.
- **Publishing needs a concrete contract.** A test can await an interface contract the application publishes, but cannot publish one: MassTransit publishes an instance.
- **The published history is run-wide.** In a parallel suite, put a test-owned value in each payload (for example from `context.UniqueName`), so one test's message cannot satisfy another test's await.
- **One application and one broker per run.** The first adapter registered wins. A suite that needs two buses uses two runs.

## Learn more

- [Messaging](./index.md): the client, destinations, attachments and the adapter contract.
- [Skip conditions](../../foundation/skip-conditions.md): capability gates and their reasons.
- [ProtoTrace](../../observability/prototrace.md): how operations and observations land in the trace.
