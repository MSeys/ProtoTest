---
sidebar_position: 12
title: Adapter contract
description: "How a broker adapter plugs into the messaging surface: the broker, the per-test consumer, and the await base."
---

# Adapter contract

Write a broker adapter to connect your messaging technology to ProtoTest's publish and await API. The adapter supplies the broker client and a consumer for each test.

## The adapter contract

An adapter implements two interfaces. ProtoTest owns the broker resource and the test-side API. The adapter owns the client technology and maps its messages to `ProtoMessage`. Broker-specific types stay out of the test API and trace:

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
    // Matches the routing key the transport recorded; a default implementation filters the predicate.
    ValueTask<ProtoMessage> AwaitAsync(string destination, string? routingKey,
        Func<ProtoMessage, bool> predicate, TimeSpan timeout, CancellationToken cancellationToken = default);
}
```

### Own resources at the right lifetime

One broker serves the whole run and must support concurrent publishes. `CreateConsumerAsync` returns a consumer that belongs to exactly one test. Never share that consumer between tests.

The test disposes its consumer during teardown. `DisposeAsync` removes the resources the adapter declared for that test, such as RabbitMQ's per-test tap queues. A tap is a consumer's private copy of messages from a destination. Separate taps let parallel tests observe the same destination without taking messages from each other.

### Prepare destinations before the test acts

`PrepareAsync` prepares the destinations the test will await before the system under test publishes. The in-memory broker keeps history, so preparation needs no broker resources. It rejects queue destinations because it has no queues.

Prepares may overlap, both within one call and across separate calls. Each destination must own the resources it declares. For example, the RabbitMQ adapter gives each tap its own channel.

Attempt every requested destination, even when one fails. Report preparation failures to the caller while allowing other destinations to finish preparation. During test setup, ProtoTest saves each failure against its destination. An await on that destination rethrows the adapter's error. Other destinations remain usable.

### Declare destinations the suite owns

`DeclareAsync` creates destinations requested through `Declare` before ProtoTest prepares the first tap. Declaration must be idempotent: leave an existing destination with compatible properties unchanged, and treat repeated declarations as no-ops. RabbitMQ rejects a declaration when the existing destination has incompatible properties.

An adapter whose destinations always exist, such as the in-memory broker, implements declaration as a no-op. An adapter that cannot create destinations must refuse the request. The default interface implementation throws `NotSupportedException` and names the adapter. A failed declaration fails test setup.

## Writing an adapter

For a working example, read `tests/ProtoTest.Extensibility.Tests/MinimalBroker.cs` and its registration in `tests/ProtoTest.Extensibility.Tests/PublicSeamTests.cs`. They use only public APIs, like a separate adapter package.

Derive the consumer from `ProtoMessageConsumerBase` and supply an `IProtoMessageAwaitSource` over the adapter's message storage. The base owns the await queue:

```csharp
public abstract class ProtoMessageConsumerBase : IProtoMessageConsumer
{
    protected ProtoMessageConsumerBase(long position);
    protected ValueTask<ProtoMessage> AwaitAsync(string destination, Func<ProtoMessage, bool> predicate,
        TimeSpan timeout, CancellationToken cancellationToken, Func<IProtoMessageAwaitSource> sourceFactory);
    protected static Func<ProtoMessage, bool> MatchRoutingKey(Func<ProtoMessage, bool> predicate, string? routingKey);
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

The consumer's public `AwaitAsync` prepares the destination if needed, such as by declaring a tap or resolving the harness. It then calls the protected overload with a source factory. The base resolves that factory inside its await gate, which allows only one await to scan the source at a time.

### Supply snapshots and waits

`SnapshotAsync` returns candidate deliveries in arrival order. Each `ProtoMessageAwaitEntry` contains one delivery and its position. The snapshot also carries a wake-up task when the source needs one. Arm that wake-up during the snapshot so a delivery between the scan and the wait cannot go unnoticed.

`WaitAsync` returns when a delivery may have arrived or the remaining time expires. Cancellation must throw. Returning at the deadline lets the base scan once more, so a delivery arriving at that instant can still match.

The consumer's starting position is the first delivery it may consume. The in-memory consumer starts after the history position captured at its creation. An empty tap starts at zero. If the source changes, call `Reset` to set the new starting position and clear the consumed set.

### Let the base manage awaits

The base serializes awaits on each consumer. It consumes each matching delivery once per destination and leaves unmatched deliveries available to later awaits. Cancellation releases the await gate. If the final scan finds no match within the timeout, the base throws `TimeoutException`.

Do not implement another await gate, position tracker, consumed set or deadline rescan in the adapter.

## Limits

- RabbitMQ exchange taps keep separate copies of deliveries. They can still receive messages published by other tests. Use predicates that identify the messages your test expects.
- Readers of a named RabbitMQ queue compete for its messages. Disposing its consumer releases the channel but leaves the existing queue in place.
- Messaging setup currently passes `CancellationToken.None` to declaration, consumer creation and preparation. Await operations accept a cancellation token.

## Links

- [Messaging](./index.md) - publish, await, destinations, and the trace shape.
- [MassTransit](./masstransit.md) - the harness bridge built on this contract.
