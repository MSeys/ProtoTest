---
sidebar_position: 10
title: Messaging
description: "Publish a message, then await the one that matters with a predicate and a timeout, on RabbitMQ or your own broker adapter."
---

# Messaging

`ProtoTest.Messaging` gives each test a broker client: publish a message to a destination, then await the one that matters with a predicate and a timeout. Failing to arrive is a `TimeoutException` and a test failure, not a sleep. Without an adapter the client runs against an in-memory broker, so the API works anywhere; `ProtoTest.Messaging.RabbitMq` replaces it with RabbitMQ, `ProtoTest.Messaging.RabbitMq.Testcontainers` owns a broker for the whole run, and `ProtoTest.Messaging.MassTransit` bridges the surface to an in-process application's MassTransit test harness - or, through its `MassTransitEnvelope` helper, speaks the MassTransit wire envelope over any adapter, published applications included.

## Install

```bash
dotnet add package ProtoTest.Messaging
dotnet add package ProtoTest.Messaging.RabbitMq
dotnet add package ProtoTest.Messaging.RabbitMq.Testcontainers
dotnet add package ProtoTest.Messaging.MassTransit
```

The packages target .NET 8, 9 and 10 (the project template defaults to `net10.0`; pass `-f net8.0` or `net9.0` for an older runtime). The first is the capability. The others are optional adapters: add RabbitMQ to talk to a real broker (with the container package when the run should start one), or `ProtoTest.Messaging.MassTransit` to use an in-process application's MassTransit test harness — see [MassTransit](./masstransit.md). `ProtoTest.Messaging.RabbitMq` and `ProtoTest.Messaging.MassTransit` bring `ProtoTest.Messaging` with them.

## Registering

```csharp
builder.AddMessaging();                                     // in-memory broker
builder.AddMessaging(messaging => messaging.UseRabbitMq()); // RabbitMQ
builder.AddMessaging(messaging => messaging.UseMassTransit<Program>()); // the app's MassTransit harness
```

```csharp
IProtoHostBuilder AddMessaging(this IProtoHostBuilder builder, Action<ProtoMessagingBuilder>? configure = null);

ProtoMessagingBuilder UseBroker(this ProtoMessagingBuilder messaging,
    Func<IServiceProvider, IProtoMessageBroker> factory);

ProtoMessagingBuilder UseBrokerUnlessConfigured(this ProtoMessagingBuilder messaging,
    Func<IServiceProvider, IProtoMessageBroker> factory, params string[] addressKeys);

ProtoMessagingBuilder CaptureAttachments(this ProtoMessagingBuilder messaging,
    Action<MessagingAttachmentOptions>? configure = null);

ProtoMessagingBuilder Tap(this ProtoMessagingBuilder messaging,
    params string[] destinations);

ProtoMessagingBuilder Declare(this ProtoMessagingBuilder messaging,
    params string[] destinations);

ProtoMessagingBuilder UseRabbitMq(this ProtoMessagingBuilder messaging,
    Action<RabbitMqOptions>? configure = null);

ProtoMessagingBuilder UseMassTransit<TProgram>(this ProtoMessagingBuilder messaging,
    string application = "Default");
```

`UseBroker` is the adapter seam; `UseRabbitMq` and `UseMassTransit` are the built-in implementations of it. `UseBroker` declares the `Broker` capability only while at least one of its `addressKeys` can provide an address, while `UseBrokerUnlessConfigured` declares it only while none is configured — the in-process direction the MassTransit bridge needs, because a configured `BaseUrl` means the application runs published with no test harness here. `Tap` declares the destinations this suite awaits, in code, so an adapter can bind each test's tap during setup — see [Destinations](#destinations). `Declare` declares the destinations this suite owns, in code, so an adapter creates them during setup before any tap binds — see [Suite-owned topology](#suite-owned-topology). `AddMessaging` registers the options, the `ProtoMessageClient` initializer for every test, the run-scoped `messaging:broker` resource, and — only when an adapter is configured — the `Messaging` capability with kind `broker`.

A repeated `AddMessaging` is not a no-op: its `configure` callback always runs, so a later call can add an adapter to an adapter-less first call or extend attachment options. Infrastructure stays idempotent — one options object, one broker holder, one initializer, one capability and one run resource — and the first adapter configured wins. A call whose `configure` throws leaves no guard behind, so a later successful call still composes.

:::tip[The in-memory broker is a test double, not a broker capability]
A configured adapter is what makes the `Broker` capability true. The in-memory default registers none, so `[RequiresCapability(ProtoCapabilityKinds.Broker)]` skips where no real broker is configured instead of passing against the double (see [skip conditions](../../foundation/skip-conditions.md)).
:::

## Options and keys

| Key | Option | Type | Default |
| --- | --- | --- | --- |
| `ProtoTest:Messaging:DefaultTimeout` | `MessagingOptions.DefaultTimeout` | `TimeSpan` | 10 seconds |
| `ProtoTest:Messaging:Destinations` | `MessagingOptions.Destinations` | `IList<string>` | empty |
| `ProtoTest:Messaging:DeclaredDestinations` | `MessagingOptions.DeclaredDestinations` | `IList<string>` | empty |
| `ProtoTest:Messaging:Attachments:CapturePublishedPayloads` | `MessagingAttachmentOptions.CapturePublishedPayloads` | `bool` | `true` |
| `ProtoTest:Messaging:Attachments:CaptureReceivedPayloads` | `MessagingAttachmentOptions.CaptureReceivedPayloads` | `bool` | `true` |
| `ProtoTest:Messaging:Attachments:RedactSensitiveData` | `JsonDiagnosticOptions.RedactSensitiveData` | `bool` | `true` |
| `ProtoTest:Messaging:Attachments:MaxDiagnosticBodyLength` | `JsonDiagnosticOptions.MaxDiagnosticBodyLength` | `int` | 65536 (64 KiB) |
| `ProtoTest:Messaging:Attachments:SensitiveJsonProperties` | `JsonDiagnosticOptions.SensitiveJsonProperties` | `List<string>` | `password`, `token`, `access_token`, `refresh_token`, `secret`, `apiKey`, `api_key`, `authorization`, `cookie`, `connectionString`, `clientSecret` |
| `ProtoTest:Messaging:RabbitMq:ConnectionString` | `RabbitMqOptions.ConnectionString` | `string` | `amqp://guest:guest@localhost:5672/` |

`MessagingAttachmentOptions` derives from `JsonDiagnosticOptions` and binds from `ProtoTest:Messaging:Attachments`; `RabbitMqOptions` binds from `ProtoTest:Messaging:RabbitMq`. Code configuration runs first and the configuration section binds over it. For the connection string the order is: an explicit configuration value wins, then the value a started container filled, then your code callback or the default. `Destinations` and `DeclaredDestinations` are lists, so configuration adds its entries after the code-declared ones and the set a test prepares or declares is deduped.

`ProtoTest:Messaging:Broker` is **not** a library option. The [demo](../../getting-started/environments.md) reads it itself (`ProtoTest:Messaging:Broker=container`) to decide whether to register a container; the messaging packages never look at that key.

## The test-side API

```csharp
public static ProtoMessageClient Messaging(this ProtoExecutionContext context, string? name = null);
```

```csharp
Task PublishAsync(string destination, string? payload = null,
    IReadOnlyDictionary<string, string?>? headers = null, string? contentType = null,
    CancellationToken cancellationToken = default);

Task<ProtoMessage> AwaitAsync(string destination, Func<ProtoMessage, bool> predicate,
    TimeSpan? timeout = null, CancellationToken cancellationToken = default);
```

`AwaitAsync` returns the first message on the destination that matches the predicate; when no timeout is given it uses `MessagingOptions.DefaultTimeout`. `Messaging()` throws when the host was not composed with `AddMessaging`; `Messaging("name")` names the run's broker client (`Default`) or fails with *"No messaging client named '…' is registered. AddMessaging registers the run's broker client under 'Default'; …"*. An empty destination or a null predicate is an argument error. `ProtoMessage` is the broker-agnostic shape every adapter maps onto:

```csharp
public sealed record ProtoMessage(string Destination, string? Payload = null,
    IReadOnlyDictionary<string, string?>? Headers = null, string? ContentType = null);
```

The payload reads are typed: `message.ReadAsJson<T>()` deserializes with `ProtoJsonDefaults.Reader` (case-insensitive property names) and returns `default` for an empty payload; `message.ReadRequired<T>()` and `message.ReadRequired<T>(jsonPath)` return `T` and throw `MessagingAssertionException` naming the destination when the payload is empty, JSON `null`, or the path is missing. The path subset is the shared one (`$`, dot members, `[n]` indices) from [REST responses](../rest/responses.md#reading-one-value-by-path). `Payload` stays available for raw inspection.

`message.Should.MatchShape(shape)` matches the payload with the same [shape matcher](../rest/responses.md#matchshape) as REST, GraphQL and gRPC. It records an `assert.json.shape` operation with a `messaging.contract.shape` observation on the ambient test context; a mismatch throws `MessagingAssertionException` whose message starts with the destination, keeping the shared `JsonShapeMismatchException` as `InnerException`, and a payload that is empty or not JSON fails the same way naming the destination.

`message.Should.MatchShape(shape, exact: true)` is the exhaustive form: a field present in the payload that the shape does not mention is a mismatch naming that field. A value constraint mentions its whole subtree. The [shape matching page](../../foundation/shape-matching.md#exact-matching) has the rules.

### The adapter contract

An adapter implements two interfaces. The capability owns the broker resource and the test-side API; an adapter owns the client technology, and no broker-specific type reaches the test or the trace:

```csharp
public interface IProtoMessageBroker
{
    string Name { get; }
    ValueTask PublishAsync(ProtoMessage message, CancellationToken cancellationToken = default);
    ValueTask<IProtoMessageConsumer> CreateConsumerAsync(CancellationToken cancellationToken = default);
    ValueTask DeclareAsync(IReadOnlyCollection<string> destinations, CancellationToken cancellationToken = default);
}

public interface IProtoMessageConsumer : IAsyncDisposable
{
    ValueTask PrepareAsync(IReadOnlyCollection<string> destinations, CancellationToken cancellationToken = default);
    ValueTask<ProtoMessage> AwaitAsync(string destination, Func<ProtoMessage, bool> predicate,
        TimeSpan timeout, CancellationToken cancellationToken = default);
}
```

One broker is shared by the whole run and may publish concurrently; `CreateConsumerAsync` returns a consumer that belongs to exactly one test. `PrepareAsync` declares the destinations that test will await *before* it acts; `DisposeAsync` removes whatever the adapter declared for it. That is the isolation contract: parallel tests on one destination cannot steal each other's messages. The in-memory broker keeps history instead and treats `PrepareAsync` as a no-op.

`DeclareAsync` creates the destinations this suite owns — those declared with `Declare` — before the first tap is prepared. Declaration is idempotent: an existing destination is left as it is and a repeated declaration is a no-op. A broker whose destinations always exist (the in-memory broker) implements it as a no-op, and an adapter that cannot create a destination refuses instead of pretending: the default interface implementation throws `NotSupportedException` naming the adapter, so a `Declare` against it fails setup loudly.

#### Writing an adapter

An adapter's consumer derives from `ProtoMessageConsumerBase` and supplies only a source; the base owns the await queue the contract above describes:

```csharp
public abstract class ProtoMessageConsumerBase : IProtoMessageConsumer
{
    protected ProtoMessageConsumerBase(long position);
    protected ValueTask<ProtoMessage> AwaitAsync(string destination, Func<ProtoMessage, bool> predicate,
        TimeSpan timeout, CancellationToken cancellationToken, Func<IProtoMessageAwaitSource> sourceFactory);
    protected void Reset(long position);
    // PrepareAsync/DisposeAsync are virtual no-ops; a tap-based adapter overrides them.
}

public interface IProtoMessageAwaitSource
{
    ValueTask<ProtoMessageAwaitSnapshot> SnapshotAsync(string destination, long position,
        CancellationToken cancellationToken);
    ValueTask WaitAsync(ProtoMessageAwaitSnapshot snapshot, TimeSpan remaining, CancellationToken cancellationToken);
}
```

An adapter's public `AwaitAsync` does whatever per-destination work it needs — declaring a tap, resolving the harness — and then calls the protected overload, which resolves the source factory inside the base's await gate. `ProtoMessageAwaitSnapshot` carries the candidates (`ProtoMessageAwaitEntry`: one delivery with its position) plus the wake-up that ends the next wait, when the source has one; the position is the first delivery the consumer may consume, so the in-memory broker starts after the shared history's creation position (`Reset` re-baselines a replaced source) while an empty tap starts at zero. An adapter never implements its own await gate, position, consumed set or deadline rescan.

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

    message.Should.MatchShape(new { id = 42 });
}
```

## Going further

### RabbitMQ topology

`UseRabbitMq` publishes to the exchange named like the destination, with the destination as the routing key, `ContentType` defaulting to `application/json` and messages marked non-persistent; null headers are dropped.

The broker owns one connection and one publish channel, created lazily on first use and kept for the run. Every test's consumer owns one channel per tap queue on that connection — channels are not thread-safe, so each side serializes its own. The consumer declares one exclusive, auto-delete tap queue per destination, named `prototest-{guid}`: during setup for a declared destination, just in time at the first await otherwise. Each queue is bound with the destination as routing key and with `#`, which covers every exchange type — direct exchanges match the routing key, topic exchanges match the `#` catch-all, and fanout and headers exchanges ignore the routing key, so their argument-less bindings match every message. Every queue is deleted when the test's consumer is disposed, and an exclusive queue never competes with the application's own consumers.

A tap binds only to an exchange that exists; the adapter never guesses. The application declares its topology before messaging prepares — the demo declares its event exchanges at application startup and registers `AddMessaging` last on purpose, so the messaging initializer binds after the in-process application's initializer has created them:

```csharp
builder
    .AddApplication(NorthstarTargets.Api, app => app.AddAspNetCoreServer<Program>())
    .AddMessaging(messaging => messaging.UseRabbitMq());
```

A suite that owns the broker itself declares the destinations it owns instead.

### Suite-owned topology

When the run owns the broker and publishes its own events, nothing else declares the destinations and `Declare` is the documented path:

```csharp
builder.AddMessaging(messaging => messaging
    // The suite owns these destinations: create them before any tap binds or the act publishes.
    .Declare("invoice.paid", "invoice.shipped")
    .Tap("invoice.paid")
    .UseRabbitMq());
```

`Declare` creates each destination on the broker during test setup, before any tap is prepared: a **fanout, durable, non-auto-delete exchange** — the shape the sample application declares for its event exchanges — sent to the broker once per run. A declaration is idempotent: a destination that already exists with that shape is left as it is, repeated `Declare` calls are deduped, and every later test repeats a no-op. A declaration the broker refuses — the name already exists with another type or durability — fails setup with the destination named, instead of letting tests publish into a mismatch. Declare the destinations this suite owns, not the application's: declaring one the application declares with other properties is a conflict, not a fix.

A destination whose exchange the application and the suite both leave undeclared fails only the tests that await it, not the whole class: preparing that tap cannot bind, and the first `AwaitAsync` on the destination throws the named error while the other tests run normally.

### Repeats and consumption

Each await consumes the message it matches. Awaits on one consumer are serialized in call order, and a delivery that matches no awaited predicate is not consumed: it stays available to a later await on the same consumer, so concurrent awaits on one destination neither lose nor steal each other's messages and every matched message is consumed exactly once. The in-memory broker behaves the same way: messages live for the run and are ordered, each consumer snapshots the broker position when it is created — so only messages published after its test started can match — and each matched message is consumed once. A predicate that throws fails only the await that owns it. A timeout is a `TimeoutException`; awaiting on a tap whose exchange is missing is an `InvalidOperationException` naming the destination, and a `Declare` the broker refuses fails setup with the destination named; an unreachable broker is an `InvalidOperationException` naming the sanitized address and `ProtoTest:Messaging:RabbitMq:ConnectionString`.

### Owning a broker

When the run should start RabbitMQ itself, register the container as [infrastructure](../../foundation/infrastructure.md) and let the host fill the keys:

```csharp
builder.AddInfrastructure(
    RabbitMqBroker.Container(),
    RabbitMqOptions.ConnectionStringSetting,   // ProtoTest:Messaging:RabbitMq:ConnectionString
    "Messaging:RabbitMq:ConnectionString");    // what the application reads

builder.AddMessaging(messaging => messaging
    // The suite owns the broker: declare the destinations it publishes to itself.
    .Declare("invoice.paid")
    .Tap("invoice.paid")
    .UseRabbitMq());
```

`AddInfrastructure` starts the container with the host and fills every key with the started connection string, so the adapter and the application under test reach the same broker. It starts before any test-level skip condition is evaluated, so a machine without a container runtime fails the run at start. `RabbitMqBroker.Container()` creates the resource without starting it; `Start()` starts now or throws with the reason; `TryStart(configure)` reports the reason in its result instead, for a fixture that decides before registering infrastructure. The default image is `rabbitmq:3`, configurable through the builder passed to `Container`. Registering with `AddResource` only owns the release — it neither starts the container nor fills settings. With no application initializer to declare the event exchanges, the suite declares its own with `Declare` (see [Suite-owned topology](#suite-owned-topology)) — no raw broker client in the suite.

### MassTransit bridge

An application that composes `AddMassTransitTestHarness` can be the broker itself: `ProtoTest.Messaging.MassTransit` publishes and awaits over the application's in-process `ITestHarness`, so the events the application publishes through its own `IPublishEndpoint` are the ones a test awaits. A destination names a message contract type (its full name, short name or `urn:message:` URN); `Declare` is a no-op because MassTransit owns message topology; and the `Broker` capability is declared only while the application's `BaseUrl` is not configured, so a published application skips instead of failing. For a published application - or any suite that talks to the broker itself - the package's `MassTransitEnvelope` builds and reads the MassTransit wire envelope (`application/vnd.masstransit+json`) through whichever adapter is configured, no harness needed. The [MassTransit page](./masstransit.md) has the registration, the ordering rule, the envelope interop and the limits. There is no container package for the bus: MassTransit is a bus library, not a server, so a harness-mode suite needs none, and an envelope-mode suite over RabbitMQ starts `ProtoTest.Messaging.RabbitMq.Testcontainers` (see [Owning a broker](#owning-a-broker)).

### Attachments

`CaptureAttachments` records every published payload and every payload matched by an await as a test attachment:

```csharp
builder.AddMessaging(messaging => messaging.CaptureAttachments());
```

A publish attaches `message-publish-{destination}-{sequence}-payload` after the broker call succeeded; a matched await attaches `message-receive-{destination}-{sequence}-payload`. `{sequence}` is the client's per-test capture number, so repeated captures on one destination stay distinct. Payloads are redacted with the shared JSON rules (`password`, `token`, `secret`, … become `[REDACTED]`) and truncated to `MaxDiagnosticBodyLength`; the trace's `Message` section is redacted with the same rules. An explicit content type wins, otherwise a payload starting with `{` or `[` is `application/json` and everything else is `text/plain`. Without `CaptureAttachments` nothing is captured. Capture never fails the operation: a failed capture is a `messaging.attachment.failed` event and the publish or await still succeeds.

## Destinations

Destinations a suite awaits are declared before the run, so the RabbitMQ adapter can bind each test's own tap during setup. Declare them in code with `Tap`:

```csharp
builder.AddMessaging(messaging => messaging
    .CaptureAttachments()
    // Pre-bind the test's tap before the system under test publishes: the worker can publish
    // invoice.issued before a test reaches its first AwaitAsync.
    .Tap("invoice.issued", "invoice.paid")
    .UseRabbitMq());
```

`Tap` takes one or more destinations; repeated calls compose and values already declared are not added twice. Configuration under `ProtoTest:Messaging:Destinations` still binds over the code values, so an environment can add its own:

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

From the moment a tap is declared, anything the application publishes is queued for that test, so the usual act-then-await order works. Binding at await time instead would miss everything published in between — which is exactly what happens for an undeclared destination, where the consumer binds just in time and can only see later messages. `Tap` is a reliability declaration: pre-bind every destination the act publishes to. The in-memory broker needs no declaration because it keeps its own history.

A destination the suite itself owns — nothing else declares it — must exist before a tap binds and before the suite publishes to it. Declare it with `Declare`:

```csharp
builder.AddMessaging(messaging => messaging
    // The suite publishes its own events on these destinations: create them at prepare.
    .Declare("invoice.paid", "invoice.shipped")
    .Tap("invoice.paid")
    .UseRabbitMq());
```

`Declare` is idempotent: the destination is created once per run, during setup, before any tap is prepared (on RabbitMQ, as a fanout, durable, non-auto-delete exchange); a destination that already exists with the same shape is left as it is and a repeated declaration is a no-op. Like `Tap`, its values bind with `ProtoTest:Messaging:DeclaredDestinations` over the code ones. A declaration is configuration, not a runtime call — the builder is consumed when `AddMessaging` runs, so a destination cannot be declared after a test has prepared. The in-memory broker treats a declaration as a no-op because every destination already exists there.

## Tracing

Every publish and await is recorded:

- Operations `messaging.publish` and `messaging.await` with `messaging.system` (the broker name), `messaging.destination`, and `messaging.timeout_ms` on the await. The payload is recorded as a redacted `Message` code section.
- Observations `messaging.published` for a successful publish and `messaging.receive` for a matched await — target is the broker name (`InMemory` or `RabbitMQ`), identifier is the destination, metadata carries `messaging.system`. `Should.MatchShape` adds a `messaging.contract.shape` observation carrying `MessagingShapeMatchData(Destination, MatchedProperties)`.
- Resources: the run-scoped `messaging:broker` resource with kind `broker`, and the per-test `messaging:consumer:Default` resource with kind `consumer`. The container adds `broker:rabbitmq`.
- The event `messaging.attachment.failed` with `attachment.name` when a capture cannot be registered.

The observations are trace evidence, not a coverage promise: `ProtoTest.Messaging` ships **no collector**, so destinations are never aggregated into a report unless you register a collector of your own with the broker's target name (see [coverage](../../observability/coverage.md)).

## Skip

`[RequiresCapability(ProtoCapabilityKinds.Broker)]` guards tests that need a real broker, with a reason naming the configuration key:

```csharp
[RequiresCapability(
    ProtoCapabilityKinds.Broker,
    Reason = "No broker is configured; set ProtoTest:Messaging:RabbitMq:ConnectionString.")]
```

`AddMessaging` registers the `Messaging` capability only when an adapter is configured. With `UseRabbitMq` the capability is conditional on `ProtoTest:Messaging:RabbitMq:ConnectionString`: a run with a configured key or a broker container that declares it keeps the capability, while a run with neither loses it and gated tests skip instead of failing at setup or first publish. A callback that sets `RabbitMqOptions.ConnectionString` in code provides the address without a key and keeps the capability; an adapter registered through `UseBroker` without address keys keeps the unconditional declaration.

## Limits

- **The in-memory broker is a test double.** It registers no `Broker` capability; its `PrepareAsync` and its `DeclareAsync` do nothing, because every destination already exists there.
- **No history on RabbitMQ.** A tap holds only what arrived after it was declared; a destination declared just in time at the await sees only later messages.
- **An unmatched delivery stays for a later await.** A delivery that matched no awaited predicate is kept for a later await on the same consumer rather than consumed, so concurrent awaits on one destination cannot steal each other's messages; disposing the consumer drops whatever it never matched. Match on the destination and the start of the payload rather than re-awaiting a message another await already consumed.
- **UTF-8 strings only.** `ProtoMessage.Payload` is a `string?`; there is no binary payload API.
- **`Tap` and `Declare` are run-scoped.** They declare destinations for every test in the run; there is no per-test destination declaration on `ProtoMessageClient`.
- **`Declare` covers the destination, not a binding.** On RabbitMQ it creates the exchange the adapter publishes to; the per-test tap queue and its bindings stay the adapter's. A queue or an `(exchange, routingKey)` pair still cannot be addressed through the API.
- **Configuration adds to `Tap` and `Declare`, it does not replace them.** Because `Destinations` and `DeclaredDestinations` are lists, an environment that exports `ProtoTest__Messaging__Destinations__0` adds a destination; it cannot withdraw a code-declared one.
- **Capture is opt-in.** Payload attachments exist only after `CaptureAttachments`.
- **Destinations are evidence, not coverage.** No Messaging collector ships (a decision, not a gap); the `messaging.published`, `messaging.receive` and `messaging.contract.shape` observations reach a report only through a collector a suite registers.
- **One run connection, serialized consumers.** RabbitMQ uses a single connection and one publish channel; every consumer owns a channel per tap queue and awaits on one consumer serialize in call order. A tap's exchange must exist — the application declares its topology, or the suite declares its own with `Declare` — and there is no retry or backoff.

## Links

- The demo's full journey: [`samples/ProtoTest.Demo/MessagingJourney.cs`](../../../../samples/ProtoTest.Demo/MessagingJourney.cs) and the host wiring in [`samples/ProtoTest.Demo/Setup.cs`](../../../../samples/ProtoTest.Demo/Setup.cs).
- Recipe: [API publishes an event](../../recipes/api-publishes-an-event.md).
- Related: [Coverage and observations](../../observability/coverage.md), [ProtoTrace](../../observability/prototrace.md), [Infrastructure](../../foundation/infrastructure.md), [Skip conditions](../../foundation/skip-conditions.md).
