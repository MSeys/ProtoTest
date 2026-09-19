---
sidebar_position: 10
title: Messaging
description: "Publish a message, then await the one that matters with a predicate and a timeout, on RabbitMQ or your own broker adapter."
---

# Messaging

`ProtoTest.Messaging` gives each test a broker client: publish a message to a destination, then await the one that matters with a predicate and a timeout. Failing to arrive is a `TimeoutException` and a test failure, not a sleep. Without an adapter the client runs against an in-memory broker, so the API works anywhere; `ProtoTest.Messaging.RabbitMq` replaces it with RabbitMQ, and `ProtoTest.Messaging.RabbitMq.Testcontainers` owns a broker for the whole run.

## Install

```bash
dotnet add package ProtoTest.Messaging
dotnet add package ProtoTest.Messaging.RabbitMq
dotnet add package ProtoTest.Messaging.RabbitMq.Testcontainers
```

The packages target .NET 8, 9 and 10 (the project template defaults to `net10.0`; pass `-f net8.0` or `net9.0` for an older runtime). The first is the capability. The other two are optional: add the RabbitMQ adapter to talk to a real broker, and the container package when the run should start one. `ProtoTest.Messaging.RabbitMq` brings `ProtoTest.Messaging` with it.

## Registering

```csharp
builder.AddMessaging();                                     // in-memory broker
builder.AddMessaging(messaging => messaging.UseRabbitMq()); // RabbitMQ
```

```csharp
IProtoHostBuilder AddMessaging(this IProtoHostBuilder builder, Action<ProtoMessagingBuilder>? configure = null);

ProtoMessagingBuilder UseBroker(this ProtoMessagingBuilder messaging,
    Func<IServiceProvider, IProtoMessageBroker> factory);

ProtoMessagingBuilder CaptureAttachments(this ProtoMessagingBuilder messaging,
    Action<MessagingAttachmentOptions>? configure = null);

ProtoMessagingBuilder UseRabbitMq(this ProtoMessagingBuilder messaging,
    Action<RabbitMqOptions>? configure = null);
```

`UseBroker` is the adapter seam; `UseRabbitMq` is the built-in implementation of it. `AddMessaging` registers the options, the `ProtoMessageClient` initializer for every test, the run-scoped `messaging:broker` resource, and — only when an adapter is configured — the `Messaging` capability with kind `broker`.

A repeated `AddMessaging` is not a no-op: its `configure` callback always runs, so a later call can add an adapter to an adapter-less first call or refresh attachment options. Infrastructure stays idempotent — one options object, one broker holder, one initializer, one capability and one run resource — and the first adapter configured wins. A call whose `configure` throws leaves no guard behind, so a later successful call still composes.

:::tip[The in-memory broker is a test double, not a broker capability]
A configured adapter is what makes the `Broker` capability true. The in-memory default registers none, so `[RequiresCapability(ProtoCapabilityKinds.Broker)]` skips where no real broker is configured instead of passing against the double (see [skip conditions](../../foundation/skip-conditions.md)).
:::

## Options and keys

| Key | Option | Type | Default |
| --- | --- | --- | --- |
| `ProtoTest:Messaging:DefaultTimeout` | `MessagingOptions.DefaultTimeout` | `TimeSpan` | 10 seconds |
| `ProtoTest:Messaging:Destinations` | `MessagingOptions.Destinations` | `IList<string>` | empty |
| `ProtoTest:Messaging:Attachments:CapturePublishedPayloads` | `MessagingAttachmentOptions.CapturePublishedPayloads` | `bool` | `true` |
| `ProtoTest:Messaging:Attachments:CaptureReceivedPayloads` | `MessagingAttachmentOptions.CaptureReceivedPayloads` | `bool` | `true` |
| `ProtoTest:Messaging:Attachments:RedactSensitiveData` | `JsonDiagnosticOptions.RedactSensitiveData` | `bool` | `true` |
| `ProtoTest:Messaging:Attachments:MaxDiagnosticBodyLength` | `JsonDiagnosticOptions.MaxDiagnosticBodyLength` | `int` | 65536 (64 KiB) |
| `ProtoTest:Messaging:Attachments:SensitiveJsonProperties` | `JsonDiagnosticOptions.SensitiveJsonProperties` | `List<string>` | `password`, `token`, `access_token`, `refresh_token`, `secret`, `apiKey`, `api_key` |
| `ProtoTest:Messaging:RabbitMq:ConnectionString` | `RabbitMqOptions.ConnectionString` | `string` | `amqp://guest:guest@localhost:5672/` |
| `ProtoTest:Messaging:RabbitMq:PollInterval` | `RabbitMqOptions.PollInterval` | `TimeSpan` | 25 ms |

`MessagingAttachmentOptions` derives from `JsonDiagnosticOptions` and binds from `ProtoTest:Messaging:Attachments`; `RabbitMqOptions` binds from `ProtoTest:Messaging:RabbitMq`. Code configuration runs first and the configuration section binds over it. For the connection string the order is: an explicit configuration value wins, then the value a started container filled, then your code callback or the default.

`ProtoTest:Messaging:Broker` is **not** a library option. The [demo](../../getting-started/environments.md) reads it itself (`ProtoTest:Messaging:Broker=container`) to decide whether to register a container; the messaging packages never look at that key.

## The test-side API

```csharp
public static ProtoMessageClient Messaging(this ProtoExecutionContext context);
```

```csharp
Task PublishAsync(string destination, string? payload = null,
    IReadOnlyDictionary<string, string?>? headers = null, string? contentType = null,
    CancellationToken cancellationToken = default);

Task<ProtoMessage> AwaitAsync(string destination, Func<ProtoMessage, bool> predicate,
    TimeSpan? timeout = null, CancellationToken cancellationToken = default);
```

`AwaitAsync` returns the first message on the destination that matches the predicate; when no timeout is given it uses `MessagingOptions.DefaultTimeout`. `Messaging()` throws when the host was not composed with `AddMessaging`. An empty destination or a null predicate is an argument error. `ProtoMessage` is the broker-agnostic shape every adapter maps onto:

```csharp
public sealed record ProtoMessage(string Destination, string? Payload = null,
    IReadOnlyDictionary<string, string?>? Headers = null, string? ContentType = null);
```

`message.ShouldMatchShape(Proto.Context, shape)` matches the payload with the same [shape matcher](../rest/responses.md#shouldmatchshape) as REST, GraphQL and gRPC. It records an `assert.json.shape` operation with a `messaging.contract.shape` observation and throws `JsonShapeMismatchException` with every mismatch listed; a payload that is empty or not JSON fails with a message naming the destination.

### The adapter contract

An adapter implements two interfaces. The capability owns the broker resource and the test-side API; an adapter owns the client technology, and no broker-specific type reaches the test or the trace:

```csharp
public interface IProtoMessageBroker
{
    string Name { get; }
    ValueTask PublishAsync(ProtoMessage message, CancellationToken cancellationToken = default);
    ValueTask<IProtoMessageConsumer> CreateConsumerAsync(CancellationToken cancellationToken = default);
}

public interface IProtoMessageConsumer : IAsyncDisposable
{
    ValueTask PrepareAsync(IReadOnlyCollection<string> destinations, CancellationToken cancellationToken = default);
    ValueTask<ProtoMessage> AwaitAsync(string destination, Func<ProtoMessage, bool> predicate,
        TimeSpan timeout, CancellationToken cancellationToken = default);
}
```

One broker is shared by the whole run and may publish concurrently; `CreateConsumerAsync` returns a consumer that belongs to exactly one test. `PrepareAsync` declares the destinations that test will await *before* it acts; `DisposeAsync` removes whatever the adapter declared for it. That is the isolation contract: parallel tests on one destination cannot steal each other's messages. The in-memory broker keeps history instead and treats `PrepareAsync` as a no-op.

## Quick start

```csharp
builder.AddMessaging();

[ProtoTest]
public async Task PayingAnInvoicePublishesAnEvent()
{
    var messages = Proto.Context.Messaging();

    await messages.PublishAsync("invoice.paid", """{"id":42}""", contentType: "application/json");

    var message = await messages.AwaitAsync(
        "invoice.paid",
        candidate => candidate.Payload!.Contains("\"id\":42"));

    message.ShouldMatchShape(Proto.Context, new { id = 42 });
}
```

## Going further

### RabbitMQ topology

`UseRabbitMq` publishes to the exchange named like the destination, with the destination as the routing key, `ContentType` defaulting to `application/json` and messages marked non-persistent; null headers are dropped.

The broker owns one connection and one publish channel, created lazily on first use and kept for the run. Every test's consumer owns its own channel on that connection — channels are not thread-safe, so each side serializes its own. The consumer declares one exclusive, auto-delete tap queue per destination, named `prototest-{guid}`: during setup for a declared destination, just in time at the first await otherwise. Each queue is bound with the destination as routing key and with `#`, which covers every exchange type — direct exchanges match the routing key, topic exchanges match the `#` catch-all, and fanout and headers exchanges ignore the routing key, so their argument-less bindings match every message. Every queue is deleted when the test's consumer is disposed, and an exclusive queue never competes with the application's own consumers.

The exchange must already exist when a tap binds; the adapter does not declare application exchanges. The demo declares its event topology at application startup and registers `AddMessaging` last on purpose, so the messaging initializer binds after the in-process application's initializer has created the exchanges:

```csharp
builder
    .AddApplication(NorthstarTargets.Api, app => app.AddAspNetCoreServer<Program>())
    .AddMessaging(messaging => messaging.UseRabbitMq());
```

### Repeats and consumption

Each await consumes the message it matches. On RabbitMQ the tap polls with `BasicGet(autoAck: true)` at `PollInterval` and discards a message whose predicate does not match, so a later await never sees it again. The in-memory broker behaves the same way: messages live for the run and are ordered, each consumer snapshots the broker position when it is created — so only messages published after its test started can match — and a match advances that consumer's position. A predicate that throws fails only the await that owns it. A timeout is a `TimeoutException`; awaiting or declaring on a missing exchange is an `InvalidOperationException` naming the destination; an unreachable broker is an `InvalidOperationException` naming the sanitized address and `ProtoTest:Messaging:RabbitMq:ConnectionString`.

### Owning a broker

When the run should start RabbitMQ itself, register the container as [infrastructure](../../foundation/infrastructure.md) and let the host fill the keys:

```csharp
builder.AddInfrastructure(
    RabbitMqBroker.Container(),
    RabbitMqOptions.ConnectionStringSetting,   // ProtoTest:Messaging:RabbitMq:ConnectionString
    "Messaging:RabbitMq:ConnectionString");    // what the application reads

builder.AddMessaging(messaging => messaging.UseRabbitMq());
```

`AddInfrastructure` starts the container with the host and fills every key with the started connection string, so the adapter and the application under test reach the same broker. It starts before any test-level skip condition is evaluated, so a machine without a container runtime fails the run at start. `RabbitMqBroker.Container()` creates the resource without starting it; `Start()` starts now or throws with the reason; `TryStart(configure, out broker, out error)` reports the reason instead, for a fixture that decides before registering infrastructure. The default image is `rabbitmq:3`, configurable through the builder passed to `Container`. Registering with `AddResource` only owns the release — it neither starts the container nor fills settings.

### Attachments

`CaptureAttachments` records every published payload and every payload matched by an await as a test attachment:

```csharp
builder.AddMessaging(messaging => messaging.CaptureAttachments());
```

A publish attaches `message-publish-{destination}-{sequence}-payload` after the broker call succeeded; a matched await attaches `message-receive-{destination}-{sequence}-payload`. `{sequence}` is the client's per-test capture number, so repeated captures on one destination stay distinct. Payloads are redacted with the shared JSON rules (`password`, `token`, `secret`, … become `[REDACTED]`) and truncated to `MaxDiagnosticBodyLength`; the trace's `Message` section is redacted with the same rules. An explicit content type wins, otherwise a payload starting with `{` or `[` is `application/json` and everything else is `text/plain`. Without `CaptureAttachments` nothing is captured. Capture never fails the operation: a failed capture is a `messaging.attachment.failed` event and the publish or await still succeeds.

## Destinations

Destinations a suite awaits should be declared before the run, so the RabbitMQ adapter can bind each test's own tap during setup:

```json
{
  "ProtoTest": {
    "Messaging": {
      "Destinations": [ "invoice.paid" ],
      "DefaultTimeout": "00:00:15"
    }
  }
}
```

From the moment a tap is declared, anything the application publishes is queued for that test, so the usual act-then-await order works. Binding at await time instead would miss everything published in between — which is exactly what happens for an undeclared destination, where the consumer binds just in time and can only see later messages. The in-memory broker needs no declaration because it keeps its own history.

## Tracing and coverage

Every publish and await is recorded:

- Operations `messaging.publish` and `messaging.await` with `messaging.system` (the broker name), `messaging.destination`, and `messaging.timeout_ms` on the await. The payload is recorded as a redacted `Message` code section.
- Observations `messaging.publish` for a successful publish and `messaging.receive` for a matched await — target is the broker name (`InMemory` or `RabbitMQ`), identifier is the destination, metadata carries `messaging.system`. `ShouldMatchShape` adds a `messaging.contract.shape` observation carrying `MessagingShapeMatchData(Destination, MatchedProperties)`.
- Resources: the run-scoped `messaging:broker` resource with kind `broker`, and the per-test `messaging:consumer:Default` resource with kind `consumer`. The container adds `broker:rabbitmq`.
- The event `messaging.attachment.failed` with `attachment.name` when a capture cannot be registered.

The package ships **no collector**. The observations exist, but they never reach a report without a collector of your own (see [coverage](../../observability/coverage.md)).

## Skip

`[RequiresCapability(ProtoCapabilityKinds.Broker)]` guards tests that need a real broker, with a reason naming the configuration key:

```csharp
[RequiresCapability(
    ProtoCapabilityKinds.Broker,
    Reason = "No broker is configured; set ProtoTest:Messaging:RabbitMq:ConnectionString.")]
```

`AddMessaging` registers the `Messaging` capability only when an adapter is configured. The container alone does not satisfy the condition — the capability comes from `UseRabbitMq`, not from owning a broker.

## Limits

- **The in-memory broker is a test double.** It registers no `Broker` capability and its `PrepareAsync` does nothing.
- **No history on RabbitMQ.** A tap holds only what arrived after it was declared; a destination declared just in time at the await sees only later messages.
- **A non-matching message is consumed.** Match on the destination and the start of the payload rather than re-awaiting the same message.
- **UTF-8 strings only.** `ProtoMessage.Payload` is a `string?`; there is no binary payload API.
- **Destinations are a flat list.** There is no per-test destination declaration API on `ProtoMessageClient`.
- **Capture is opt-in.** Payload attachments exist only after `CaptureAttachments`.
- **One run connection, serialized channels.** RabbitMQ uses a single connection and one publish channel, with consumer operations serialized per channel; the exchange must already exist and there is no retry or backoff.

## Links

- The demo's full journey: [`samples/ProtoTest.Demo/MessagingJourney.cs`](../../../../samples/ProtoTest.Demo/MessagingJourney.cs) and the host wiring in [`samples/ProtoTest.Demo/Setup.cs`](../../../../samples/ProtoTest.Demo/Setup.cs).
- Recipe: [API publishes an event](../../recipes/api-publishes-an-event.md).
- Related: [Coverage and observations](../../observability/coverage.md), [ProtoTrace](../../observability/prototrace.md), [Infrastructure](../../foundation/infrastructure.md), [Skip conditions](../../foundation/skip-conditions.md).
