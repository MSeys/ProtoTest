---
sidebar_position: 10
title: Test RabbitMQ and message events in .NET
sidebar_label: Messaging
description: "Publish a message, then await the one that matters with a predicate and a timeout, on RabbitMQ or your own broker adapter."
---

import SequenceLanes from '@site/src/components/SequenceLanes';

# Test RabbitMQ and message events in .NET
Each test gets a broker client. Publish a message, then await the matching message with a predicate and a timeout:

```csharp
[ProtoTest]
public async Task Paying_an_invoice_publishes_an_event()
{
    var messages = Proto.Context.Messaging();

    await messages.PublishAsync("invoice.paid", """{"id":42}""", contentType: "application/json");

    var message = await messages.AwaitAsync(
        "invoice.paid",
        candidate => candidate.MatchesShape(new { id = 42 }));

    message.Should.MatchShape(new { id = 42 });
}
```

Run it with `dotnet test`. A green run prints the passed test. A message that never arrives is a `TimeoutException` and a test failure, not a sleep.

## What it adds

A per-test messaging client that publishes and awaits messages. Without an adapter it runs against an in-memory
broker, so the API works anywhere. An adapter connects it to a real broker:

| Package | What it gives |
| --- | --- |
| `ProtoTest.Messaging` | The client: publish, await, `Tap` and `Declare`, and the in-memory broker. The in-memory broker is a test double: it keeps the run's history and registers no `Broker` capability. |
| `ProtoTest.Messaging.RabbitMq` | RabbitMQ as the adapter, with a tap queue per test and suite-owned topology through `Declare`. |
| `ProtoTest.Messaging.RabbitMq.Testcontainers` | A RabbitMQ container the run owns. See [Owning a broker](#owning-a-broker). |
| `ProtoTest.Messaging.MassTransit` | The application's in-process MassTransit test harness as the adapter, or the MassTransit wire envelope over any adapter. See [MassTransit](./masstransit.md). |

Kafka, Azure Service Bus and AWS (MSK, SQS/SNS, EventBridge) are not shipped, and none is announced. To add one,
implement `IProtoMessageBroker` (publish, one consumer per test, declare) with a consumer derived from
`ProtoMessageConsumerBase`, and register it with `UseBroker`. The test-side API, the trace and the `Broker`
capability come with it. The [Adapter contract](./adapters.md) has the details.

## Install

```bash
dotnet add package ProtoTest.Messaging
dotnet add package ProtoTest.Messaging.RabbitMq
dotnet add package ProtoTest.Messaging.RabbitMq.Testcontainers
dotnet add package ProtoTest.Messaging.MassTransit
```

The packages target .NET 8, 9 and 10. The project template defaults to `net10.0`. Pass `--framework net8.0` or
`--framework net9.0` for an older runtime. `ProtoTest.Messaging` is the only required package. The RabbitMQ and
MassTransit packages bring it with them.

## Compose

```csharp
builder.AddMessaging();                                     // in-memory broker
builder.AddMessaging(messaging => messaging.UseRabbitMq()); // RabbitMQ
builder.AddMessaging(messaging => messaging.UseMassTransit<Program>()); // the app's MassTransit harness
```

```csharp
IProtoHostBuilder AddMessaging(this IProtoHostBuilder builder, Action<ProtoMessagingBuilder>? configure = null);
```

`AddMessaging` always registers the options, the per-test client initializer and the run-scoped broker resource.
With an adapter, it also registers the `Messaging` capability with kind `broker`. `UseRabbitMq` and
`UseMassTransit` are the built-in adapters. Both use `UseBroker`, the seam for your own.

Two more calls prepare destinations during setup:

- `Tap` names the destinations the suite awaits, so each test's tap binds before the test acts. See [Destinations](#destinations).
- `Declare` names the destinations the suite owns, so the adapter creates them first. See [Suite-owned topology](#suite-owned-topology).

Calling `AddMessaging` again runs its `configure` callback again, so a later call can add an adapter or extend
the attachment options. Everything else is registered once, and the first adapter configured wins.

:::tip[The in-memory broker is a test double, not a broker capability]
A configured adapter is what makes the `Broker` capability true. The in-memory default registers none, so `[RequiresCapability(ProtoCapabilityKinds.Broker)]` skips where no real broker is configured instead of passing against the double (see [skip conditions](../../foundation/skip-conditions.md)).
:::

## The tasks

The `Paying_an_invoice_publishes_an_event` test above is the whole pattern: publish, await the match, assert the shape.

<SequenceLanes
  participants={['Test', 'Broker', 'Application']}
  steps={[
    {from: 'Test', to: 'Broker', label: <span><code>Tap</code>, in setup, before the act</span>},
    {from: 'Test', to: 'Application', label: 'acts'},
    {from: 'Application', to: 'Broker', label: 'publishes'},
    {from: 'Test', to: 'Broker', label: <span><code>AwaitAsync</code>(predicate, timeout)</span>},
    {from: 'Broker', to: 'Test', label: <span>the match, or a <code>TimeoutException</code></span>, reply: true},
  ]}
/>

The sections below cover what a suite does next:

1. [Await an event the application publishes](#destinations): tap it during setup.
2. [Own the destinations the suite publishes to](#suite-owned-topology): declare them.
3. [Read a dead-letter queue](#queue-destinations): await the queue itself.
4. [Start RabbitMQ for the run](#owning-a-broker): a container the host owns.
5. [Test a MassTransit application](#masstransit-bridge): await over its harness.

### Destinations

On RabbitMQ, a tap only sees what arrives after it binds. Name the destinations a suite awaits with `Tap`, so each
test's tap binds during setup, before the test acts:

```csharp
builder.AddMessaging(messaging => messaging
    .CaptureAttachments()
    // Pre-bind the test's tap before the system under test publishes: the worker can publish
    // invoice.issued before a test reaches its first AwaitAsync.
    .Tap("invoice.issued", "invoice.paid")
    .UseRabbitMq());
```

From that moment, anything the application publishes is queued for that test, so the act-then-await order works.
A destination without `Tap` binds only at the first await, and misses everything published before it. The
in-memory broker needs no `Tap`, because it keeps its own history.

`Tap` takes one or more destinations. Repeated calls add up, and a destination is added once. Configuration under
`ProtoTest:Messaging:Destinations` adds to the code values, so an environment can add its own:

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

A tap binds only to an exchange that exists. The application declares its topology before messaging prepares. The
demo declares its event exchanges at application startup and registers `AddMessaging` last, so the messaging
initializer binds after the in-process application has created them:

```csharp
builder
    .AddApplication(NorthstarTargets.Api, app => app.AddAspNetCoreServer<Program>())
    .AddMessaging(messaging => messaging.UseRabbitMq());
```

If neither the application nor the suite declares a tapped exchange, only the tests that await it fail. The first
`AwaitAsync` on that destination throws an error naming it, and the other tests run normally.

### Suite-owned topology

When the run owns the broker and the suite publishes its own events, nothing else creates the destinations.
`Declare` does:

```csharp
builder.AddMessaging(messaging => messaging
    // The suite owns these destinations: create them before any tap binds or the act publishes.
    .Declare("invoice.paid", "invoice.shipped")
    .Tap("invoice.paid")
    .UseRabbitMq());
```

During setup, before any tap binds, the adapter creates each declared destination once per run. On RabbitMQ that
is a **fanout, durable, non-auto-delete exchange**, the shape the sample application uses for its events.

A declaration is safe to repeat. An exchange that already exists with that shape is left as it is, and repeated
names count once. If the name already exists with another type or durability, the broker refuses, and setup fails
naming the destination.

Declare only the destinations the suite owns. Declaring one the application also declares, with other properties,
is a conflict, not a fix.

### Queue destinations

A tap reads an exchange. A dead-letter queue is a queue, fed by the product's own dead-letter bindings. Await it
directly with `ProtoDestination.Queue(name)`, which builds the destination `queue:{name}`:

```csharp
// The product's dead-letter bindings carry the poison into billing.session-ended.dlq;
// the test awaits the queue itself.
var dead = await Proto.Context.Messaging().AwaitAsync(
    ProtoDestination.Queue("billing.session-ended.dlq"),
    message => message.ReadRequired<SessionEnded>().SessionId == sessionId);
```

The adapter checks that the queue exists, consumes it on the test's own channel, and leaves the queue as it is when
the test ends. A missing queue fails with its name, like a missing exchange. `Tap` accepts a queue destination,
so consuming starts during setup. `Declare` refuses one: the component that owns the queue creates it.

Consuming a shared queue has two consequences:

- The await reads what the queue already holds, not only what arrives later.
- It removes what it reads, so another test or a live consumer no longer sees that message.

So await a queue directly when the test owns it, such as a dead-letter queue no one else reads in a serial suite.
In a parallel suite, await the exchange that feeds it.

A consumed delivery carries the queue on `ProtoMessage.Destination` and the transport's routing key on
`ProtoMessage.RoutingKey`. A keyed await on a queue filters by that recorded key. The in-memory broker and the
MassTransit harness have no queues, so they refuse a queue destination and name the exchange to await instead.

#### How awaits consume

Each await consumes the message it matches, exactly once. Awaits on one consumer run in call order. A delivery that
no awaited predicate matches is not consumed: it stays for a later await on the same consumer. Two awaits on one
destination therefore never steal each other's messages.

Consumption is tracked per destination, so an await on one destination never hides another destination's first
delivery.

The in-memory broker behaves the same way. Messages live for the run and keep their order. Each consumer starts at
the broker position when its test started, so only messages published after that can match.

#### Errors

| Situation | What happens |
| --- | --- |
| No match before the timeout | `TimeoutException` |
| The predicate throws | Only the await that owns it fails |
| A tapped exchange or a queue does not exist | `InvalidOperationException` naming the destination |
| The broker refuses a `Declare` | Setup fails, naming the destination |
| The broker is unreachable | `InvalidOperationException` naming the sanitized address and `ProtoTest:Messaging:RabbitMq:ConnectionString` |

### Owning a broker

When the run should start RabbitMQ itself, register the container as [infrastructure](../../foundation/infrastructure.md) and let the host fill the keys:

```csharp
builder.AddInfrastructure(
    "MessagingBroker",
    chain => chain
        .UseConfigured()
        .UseContainer(RabbitMqBroker.Container()),
    RabbitMqOptions.ConnectionStringSetting,   // ProtoTest:Messaging:RabbitMq:ConnectionString
    "Messaging:RabbitMq:ConnectionString");    // what the application reads

builder.AddMessaging(messaging => messaging
    // The suite owns the broker: declare the destinations it publishes to itself.
    .Declare("invoice.paid")
    .Tap("invoice.paid")
    .UseRabbitMq());
```

`UseContainer` starts the container with the host and fills every key with its connection string, so the adapter
and the application reach the same broker. The container starts before any skip condition runs, so a machine
without a container runtime fails the run at start.

`RabbitMqBroker.Container()` creates the resource without starting it. `Start()` starts it now or throws with the
reason. `TryStart(configure)` returns the reason instead, for a fixture that decides before registering
infrastructure. The default image is `rabbitmq:3`, configurable through the builder passed to `Container`.

Registering the container with `AddResource` only makes the run release it. It neither starts the container nor
fills settings. With no application to declare the exchanges, the suite declares its own with `Declare`.

### MassTransit bridge

An application that composes `AddMassTransitTestHarness` can be the broker itself. `ProtoTest.Messaging.MassTransit`
publishes and awaits over the application's in-process `ITestHarness`, so a test awaits the events the application
publishes through its own `IPublishEndpoint`.

A destination names a message contract type: its full name, short name or `urn:message:` URN. `Declare` does
nothing, because MassTransit owns its topology. The `Broker` capability exists only while the application runs
in-process (`UseBrokerWhenInProcess`), so against a published application the test skips instead of failing.

For a published application, `MassTransitEnvelope` builds and reads the MassTransit wire envelope
(`application/vnd.masstransit+json`) over whichever adapter is configured. The [MassTransit page](./masstransit.md)
has the registration, the ordering rule, the envelope and the limits.

### Attachments

`CaptureAttachments` keeps every published payload, and every payload an await matched, as a test attachment:

```csharp
builder.AddMessaging(messaging => messaging.CaptureAttachments());
```

- A publish attaches `message-publish-{destination}-{sequence}-payload` after the broker accepted it. A matched await attaches `message-receive-{destination}-{sequence}-payload`. `{sequence}` numbers the captures in one test, so repeats stay distinct.
- Payloads are redacted with the shared JSON rules (`password`, `token`, `secret` and the others become `[REDACTED]`) and cut at `MaxDiagnosticBodyLength`. The trace's `Message` section uses the same rules.
- An explicit content type wins. Otherwise a payload starting with `{` or `[` is `application/json`, and anything else is `text/plain`.
- Capture never fails the call. A failed capture is a `messaging.attachment.failed` event, and the publish or await still succeeds.

### RabbitMQ details

<details>
<summary>How the RabbitMQ adapter publishes, binds and connects</summary>

| Fact | Rule |
| --- | --- |
| Publish target | the exchange named like the destination, under the message routing key or the destination itself |
| Content | `ContentType` defaults to `application/json`, messages are non-persistent, null headers are dropped |
| Delivery | the delivery routing key fills `ProtoMessage.RoutingKey`; a keyed await binds its key on the destination tap |
| Connection | one connection and one publish channel for the run, created lazily; each test consumer owns one channel per destination |
| Exchange tap | one exclusive, auto-delete queue per destination (`prototest-{guid}`), bound with the destination key and `#`: direct exchanges match the key, topic exchanges match the `#` catch-all, fanout and headers exchanges match every message; bound during setup for a tapped destination, just in time at the first await otherwise |
| Keyed await | binds the key on the destination tap; a pre-bound tap keeps what arrived before the keyed await |
| Queue destination | consumed as it exists, checked passively and left untouched |

Each tap queue is deleted when the test consumer is disposed. An exclusive tap never competes with the
application's own consumers.

</details>

## Reference: options and API

| Key | Option | Type | Default |
| --- | --- | --- | --- |
| `ProtoTest:Messaging:DefaultTimeout` | `MessagingOptions.DefaultTimeout` | `TimeSpan` | 10 seconds |
| `ProtoTest:Messaging:Destinations` | `MessagingOptions.Destinations` | `IList<string>` | empty |
| `ProtoTest:Messaging:DeclaredDestinations` | `MessagingOptions.DeclaredDestinations` | `IList<string>` | empty |
| `ProtoTest:Messaging:Attachments:CapturePublishedPayloads` | `MessagingAttachmentOptions.CapturePublishedPayloads` | `bool` | `true` |
| `ProtoTest:Messaging:Attachments:CaptureReceivedPayloads` | `MessagingAttachmentOptions.CaptureReceivedPayloads` | `bool` | `true` |
| `ProtoTest:Messaging:Attachments:RedactSensitiveData` | `JsonDiagnosticOptions.RedactSensitiveData` | `bool` | `true` |
| `ProtoTest:Messaging:Attachments:MaxDiagnosticBodyLength` | `JsonDiagnosticOptions.MaxDiagnosticBodyLength` | `int` | 65536 (64 KiB) |
| `ProtoTest:Messaging:Attachments:SensitiveJsonProperties` | `JsonDiagnosticOptions.SensitiveJsonProperties` | `List<string>` | `password`, `token`, `access_token`, `refresh_token`, `secret`, `apiKey`, `api_key`, `authorization`, `cookie`, `connectionString`, `clientSecret`, `client_secret`, `id_token` |
| `ProtoTest:Messaging:RabbitMq:ConnectionString` | `RabbitMqOptions.ConnectionString` | `string` | `amqp://guest:guest@localhost:5672/` |

`MessagingAttachmentOptions` derives from `JsonDiagnosticOptions` and binds from `ProtoTest:Messaging:Attachments`.
`RabbitMqOptions` binds from `ProtoTest:Messaging:RabbitMq`. Code configuration runs first, and the configuration
section binds over it.

For the connection string, an explicit configuration value wins, then the value a started container filled, then
your code callback or the default. `Destinations` and `DeclaredDestinations` are lists, so configuration adds its
entries after the code ones, and each name counts once.

`ProtoTest:Messaging:Broker` is **not** a library option. The [demo](../../getting-started/environments.md) reads it
itself (`ProtoTest:Messaging:Broker=container`) to decide whether to register a container. The messaging packages
never read that key.

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

- `AwaitAsync` returns the first message on the destination that matches the predicate. Without a timeout, it uses `MessagingOptions.DefaultTimeout`.
- `Messaging()` throws when the host was composed without `AddMessaging`.
- `ProtoMessage` is the shape every adapter maps onto: `Destination`, `Payload`, `Headers`, `ContentType`, `RoutingKey`.
- `message.ReadAsJson<T>()` deserializes with `ProtoJsonDefaults.Reader` and returns `default` for an empty payload. `message.ReadRequired<T>()` and `message.ReadRequired<T>(jsonPath)` throw `MessagingAssertionException`, naming the destination, when the payload is empty, JSON `null`, or the path is missing.

```csharp
ProtoMessagingBuilder UseBroker(this ProtoMessagingBuilder messaging,
    Func<IServiceProvider, IProtoMessageBroker> factory, params string[] addressKeys);
ProtoMessagingBuilder UseBrokerWhenInProcess(this ProtoMessagingBuilder messaging,
    Func<IServiceProvider, IProtoMessageBroker> factory, string application, params string[] configuredKeys);
ProtoMessagingBuilder Tap(this ProtoMessagingBuilder messaging, params string[] destinations);
ProtoMessagingBuilder Declare(this ProtoMessagingBuilder messaging, params string[] destinations);
ProtoMessagingBuilder UseRabbitMq(this ProtoMessagingBuilder messaging, Action<RabbitMqOptions>? configure = null);
ProtoMessagingBuilder UseMassTransit<TProgram>(this ProtoMessagingBuilder messaging, string application = "Default");
```

`UseBroker` declares the `Broker` capability only while at least one of its `addressKeys` can provide an address.
`UseBrokerWhenInProcess` declares it only while the named application runs in-process.

**Routing keys.** A routing key is the address a publish takes inside the exchange, and the address a message was
delivered under. `PublishAsync(exchange, routingKey, payload)` publishes under a key, and
`AwaitAsync(exchange, routingKey, predicate, …)` matches only that key. Without one, the plain
overloads publish under the destination itself. The key is bound when the keyed await starts, so a direct exchange
delivers only messages published after that. Pre-bind the destination with `Tap` to keep act-then-await reliable.
MassTransit addresses contract types, not routing, so its adapter rejects a routing key.

**Shape checks.** `candidate.MatchesShape(shape)` picks a message in an `AwaitAsync` predicate: it answers true or false, records nothing, and is false for an empty or non-JSON payload. `message.Should.MatchShape(shape)` then asserts it with the same [shape matcher](../rest/responses.md#matchshape)
as REST, GraphQL and gRPC. `MatchShape(shape, exact: true)` is the exhaustive form. A mismatch throws
`MessagingAssertionException`, starting with the destination, with the shared `JsonShapeMismatchException` as its
`InnerException`.

An adapter implements two interfaces: the capability owns the broker resource and the test-side API, and the
adapter owns the client technology. The full contract is on the [Adapter contract](./adapters.md).

## In the trace and coverage

```text
messaging.publish · invoice.paid                    # system InMemory, payload redacted
└─ messaging.published observation (target InMemory)

messaging.await · invoice.paid + routing key "session.ended"
├─ messaging.timeout_ms = 10000
├─ tap bound during setup (Tap), or just in time at the first await
└─ messaging.receive observation on match, TimeoutException naming destination and key on timeout
```

Every publish and await is recorded:

- **Operations** `messaging.publish` and `messaging.await`, with `messaging.system` (the broker name), `messaging.destination`, `messaging.routing_key` when the call named one, and `messaging.timeout_ms` on the await. The payload is a redacted `Message` code section.
- **Observations** `messaging.published` for a successful publish and `messaging.receive` for a matched await. The target is the broker name (`InMemory` or `RabbitMQ`), the identifier is the destination, and the metadata carries `messaging.system`. `Should.MatchShape` adds a `messaging.contract.shape` observation carrying `MessagingShapeMatchData(Destination, MatchedProperties)`.
- **Resources** `messaging:broker` for the run, with kind `broker`, and `messaging:consumer:Default` per test, with kind `consumer`. The container adds `broker:rabbitmq`.
- **Event** `messaging.attachment.failed`, with `attachment.name`, when a capture cannot be registered.

These observations are trace evidence, not coverage. `ProtoTest.Messaging` ships **no collector**, so destinations
reach a report only through a collector you register with the broker's target name (see
[coverage](../../observability/coverage.md)).

## Skip

`[RequiresCapability(ProtoCapabilityKinds.Broker)]` guards tests that need a real broker, with a reason naming the configuration key:

```csharp
[RequiresCapability(
    ProtoCapabilityKinds.Broker,
    Reason = "No broker is configured; set ProtoTest:Messaging:RabbitMq:ConnectionString.")]
```

With `UseRabbitMq`, the capability depends on `ProtoTest:Messaging:RabbitMq:ConnectionString`. A run with the key,
or with a broker container that fills it, keeps the capability. A run with neither loses it, and gated tests skip
instead of failing at setup or at the first publish. A callback that sets `RabbitMqOptions.ConnectionString` in
code also keeps it. An adapter registered through `UseBroker` without address keys always declares the capability.

## Limits

- **The first adapter wins.** A repeated `AddMessaging` can add an adapter to an adapter-less call, but never replaces one.
- **The in-memory broker is a test double.** It has no `Broker` capability. Its destinations always exist, so `PrepareAsync` and `DeclareAsync` do nothing, and it refuses queue destinations.
- **No history on RabbitMQ.** An exchange tap holds only what arrived after it bound. A queue destination reads the backlog, but removes what it reads.
- **A queue await competes.** Another test or a live consumer on the same queue takes deliveries from the same backlog.
- **UTF-8 strings only.** `ProtoMessage.Payload` is a `string?`. There is no binary payload API.
- **`Tap` and `Declare` are run-scoped.** They apply to every test in the run. `ProtoMessageClient` has no per-test declaration.
- **`Declare` creates exchanges, not bindings.** The per-test tap queue and its bindings stay the adapter's. A routing key is addressed per publish and per await.
- **Configuration adds, it does not remove.** `ProtoTest__Messaging__Destinations__0` adds a destination. It cannot withdraw one declared in code.
- **Capture is opt-in.** Payload attachments exist only after `CaptureAttachments`.
- **No collector ships.** Messaging observations reach a report only through a collector the suite registers. This is a decision, not a gap.
- **One connection, no retry.** RabbitMQ uses one connection and one publish channel for the run. A tapped exchange must exist, and nothing retries or backs off.

## Links

- The sample's event journey: [`samples/Northstar.ProtoTest/BrokerJourney.cs`](../../../../samples/Northstar.ProtoTest/BrokerJourney.cs) and the host wiring in [`samples/Northstar.ProtoTest/Setup.cs`](../../../../samples/Northstar.ProtoTest/Setup.cs).
- Recipe: [API publishes an event](../../recipes/api-publishes-an-event.md).
- Related: [Coverage and observations](../../observability/coverage.md), [ProtoTrace](../../observability/prototrace.md), [Infrastructure](../../foundation/infrastructure.md), [Skip conditions](../../foundation/skip-conditions.md).
