---
sidebar_position: 10
title: Messaging
description: "Publish a message, then await the one that matters with a predicate and a timeout, on RabbitMQ or your own broker adapter."
---

# Messaging

`ProtoTest.Messaging` gives each test a broker client: publish a message to a destination, then await the one that matters with a predicate and a timeout. Failing to arrive is a `TimeoutException` and a test failure, not a sleep. Without an adapter the client runs against an in-memory broker, so the API works anywhere; `ProtoTest.Messaging.RabbitMq` replaces it with RabbitMQ, and `ProtoTest.Messaging.RabbitMq.Testcontainers` owns a broker for the whole run.

```bash
dotnet add package ProtoTest.Messaging
dotnet add package ProtoTest.Messaging.RabbitMq
dotnet add package ProtoTest.Messaging.RabbitMq.Testcontainers
```

The first package is the capability. The other two are optional: add the RabbitMQ adapter to talk to a real broker, and the container package when the run should start one.

## Registering

```csharp
builder.AddMessaging();                                    // in-memory broker
builder.AddMessaging(messaging => messaging.UseRabbitMq()); // RabbitMQ
```

`UseBroker(Func<IServiceProvider, IProtoMessageBroker> factory)` is the adapter seam; `UseRabbitMq` is the built-in implementation of it. `AddMessaging` registers the [options](#options-and-configuration), the broker as a run-scoped `messaging:broker` resource released with the host, and a `ProtoMessageClient` for every test, created during setup.

:::tip[The in-memory broker is a test double, not a broker capability]
A configured adapter is what makes the `Broker` capability true: the host registers a `Messaging` capability with kind `broker`. The in-memory default registers none, so `[RequiresCapability(ProtoCapabilityKinds.Broker)]` skips where no real broker is configured instead of passing against the double (see [skip conditions](../../foundation/skip-conditions.md)).
:::

## The test-side API

```csharp
var messages = Proto.Context.Messages();

await messages.PublishAsync(
    "invoice.paid",
    """{"id":42}""",
    contentType: "application/json");

var message = await messages.AwaitAsync(
    "invoice.paid",
    candidate => candidate.Payload!.Contains("\"id\":42"),
    TimeSpan.FromSeconds(15));
```

```csharp
Task PublishAsync(
    string destination,
    string? payload = null,
    IReadOnlyDictionary<string, string?>? headers = null,
    string? contentType = null,
    CancellationToken cancellationToken = default);

Task<ProtoMessage> AwaitAsync(
    string destination,
    Func<ProtoMessage, bool> predicate,
    TimeSpan? timeout = null,
    CancellationToken cancellationToken = default);
```

`AwaitAsync` returns the first message on the destination that matches the predicate. When no timeout is given it uses `ProtoMessagingOptions.DefaultTimeout` (10 seconds). `ProtoMessage` carries `Destination`, `Payload`, `Headers` and `ContentType`; adapters map their technology onto that shape and no broker-specific type reaches the test or the trace.

`Messages()` throws if the host was not composed with `AddMessaging`.

## Destinations

A destination is a string naming where messages go — for RabbitMQ, an exchange. Destinations a suite awaits should be declared in configuration before the run so an adapter can bind its tap before the test acts:

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

The RabbitMQ adapter binds one tap queue per declared destination during test setup, then purges it. From that point anything the application publishes is queued for the test, so the usual act-then-await order works. Binding at await time instead would miss everything published in between — which is exactly what happens for an undeclared destination, where the adapter binds just in time and can only see messages that come later. The in-memory broker needs no declaration because it keeps its own history.

The destination's exchange must already exist when the tap binds. The demo's application declares its event topology at startup and registers `AddMessaging` last on purpose, so the messaging initializer binds after the in-process application's initializer has created the exchanges:

```csharp
builder
    .AddApplication(NorthstarTargets.Api, app => app.AddAspNetCoreServer<Program>() /* …, declares invoice.paid */)
    .AddMessaging(messaging => messaging.UseRabbitMq());
```

The journey then awaits the event the application emits after an API call:

```csharp
var message = await Proto.Context.Messages().AwaitAsync(
    "invoice.paid",
    candidate => candidate.Payload is not null
        && candidate.Payload.Contains(invoice.Id.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal),
    TimeSpan.FromSeconds(15));
```

## Options and configuration

| Key | Option | Default |
| --- | --- | --- |
| `ProtoTest:Messaging:DefaultTimeout` | `ProtoMessagingOptions.DefaultTimeout` | 10 seconds |
| `ProtoTest:Messaging:Destinations` | `ProtoMessagingOptions.Destinations` | empty |
| `ProtoTest:Messaging:RabbitMq:ConnectionString` | `ProtoRabbitMqOptions.ConnectionString` | `amqp://guest:guest@localhost:5672/` |
| `ProtoTest:Messaging:RabbitMq:PollInterval` | `ProtoRabbitMqOptions.PollInterval` | 25 ms |

```csharp
builder.AddMessaging(messaging => messaging.UseRabbitMq(options =>
{
    options.ConnectionString = "amqp://guest:guest@localhost:5672/";
    options.PollInterval = TimeSpan.FromMilliseconds(25);
}));
```

Values are layered the way [configuration](../../getting-started/configuration.md) is everywhere: the options default, then your code callback, then the `ProtoTest:Messaging:RabbitMq` configuration section. A started container's connection string is applied after that when the configuration key is absent, so it wins over a programmatic value; an explicit configuration value wins over the container.

`ProtoTest:Messaging:Broker` is **not** a library option. The [demo](../../getting-started/environments.md) reads it itself (`ProtoTest:Messaging:Broker=container`) to decide whether to register a container; the messaging packages never look at that key.

## The RabbitMQ adapter

`UseRabbitMq` publishes to the exchange named like the destination, with the destination as the routing key, `ContentType` defaulting to `application/json` and messages marked non-persistent. Awaiting uses the prepared tap when the destination was declared, and otherwise declares a fresh exclusive auto-delete queue, binds it catch-all (`#`) to the exchange, drains it until a match or the timeout, then deletes it. The tap never competes with the application's own consumers.

The adapter opens one connection and one channel lazily on first use and keeps them for the run; the broker is disposed when the host is. If the broker cannot be reached, the failure is an `InvalidOperationException` naming the sanitized address and `ProtoTest:Messaging:RabbitMq:ConnectionString` — the connection string is never shown with its credentials.

## Owning a broker

When the run should start RabbitMQ itself, register the container as [infrastructure](../../foundation/infrastructure.md) and let the host fill the keys:

```csharp
builder.AddInfrastructure(
    RabbitMqBroker.Container(),
    ProtoRabbitMqOptions.ConnectionStringSetting,   // ProtoTest:Messaging:RabbitMq:ConnectionString
    "Messaging:RabbitMq:ConnectionString");         // what the application reads

builder.AddMessaging(messaging => messaging.UseRabbitMq());
```

The container is run-scoped: started once with the host and released after the run stopped and the reports were written. `Container()` creates it without starting it, so a machine without a container runtime fails the run's start; `TryStart` reports why it could not start instead (for a fallback or a skip decision), and `Start` starts now or throws. The default image is `rabbitmq:3`, configurable through the builder passed to `Container`. The started connection string reaches the adapter through `ProtoRabbitMqOptions.ConnectionStringSetting` and the application through whatever key you list, so tests and the application under test share one broker — the same mechanism the [PostgreSQL container](../sql/index.md) uses.

## Tracing

Every publish and await is recorded:

- Operations `messaging.publish` and `messaging.await` with `messaging.system` (the broker name), `messaging.destination`, and `messaging.timeout_ms` on the await. The payload is recorded as a `Message` code section.
- Observations `messaging.publish` and `messaging.receive` — target is the broker name (`InMemory` or `RabbitMQ`), identifier is the destination, metadata carries `messaging.system`. The package ships no collector for them; register one if you want destinations aggregated into a report (see [coverage](../../observability/coverage.md)).
- Resources: the run-scoped `messaging:broker` resource, plus `broker:rabbitmq` when the container is registered. Both appear in [ProtoTrace](../../observability/prototrace.md) and the run's resources.

## Limits

- **One tap per destination per run.** The RabbitMQ adapter stores a tap per declared destination on the broker, and every test setup purges it. Tests that run in parallel and await the same destination share that one tap and purge each other's backlog, so each parallel test must use a distinct destination.
- **No history on RabbitMQ.** The in-memory broker keeps every message for the run, so an await can match one published earlier (scoped to the test by its position snapshot). RabbitMQ has no such history: the tap holds only what arrived while it was bound, and each setup purges it. Messages published with no tap bound are gone for the tests.
- **A non-matching message is consumed.** The RabbitMQ await drains a message and discards it when the predicate does not match, so a later await will not see it again. Match on the destination and the start of the payload rather than re-awaiting the same message.
- **The in-memory broker is not a broker capability.** Guard tests that need a real broker with `[RequiresCapability(ProtoCapabilityKinds.Broker)]` (the demo's messaging journey does, with a reason naming the configuration key).
