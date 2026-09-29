---
sidebar_position: 12
title: Adapter contract
description: "How a broker adapter plugs into the messaging surface: the broker, the per-test consumer, and the await base."
---

# Adapter contract

## The adapter contract

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
    // Matches the routing key the transport recorded; a default implementation filters the predicate.
    ValueTask<ProtoMessage> AwaitAsync(string destination, string? routingKey,
        Func<ProtoMessage, bool> predicate, TimeSpan timeout, CancellationToken cancellationToken = default);
}
```

One broker is shared by the whole run and may publish concurrently; `CreateConsumerAsync` returns a consumer that belongs to exactly one test. `PrepareAsync` declares the destinations that test will await *before* it acts; `DisposeAsync` removes whatever the adapter declared for it. Prepares may run concurrently: a prepare for several destinations may prepare them at the same time and separate prepares may overlap, so every destination must own the resources it declares (the RabbitMQ adapter gives each tap its own channel). Every requested destination is attempted, so one that cannot be prepared does not stop the others; the setup hook records the failure on that destination and the first await on it rethrows the adapter's named error. That is the isolation contract: parallel tests on one destination cannot steal each other's messages. The in-memory broker keeps history instead and treats `PrepareAsync` as a no-op.

`DeclareAsync` creates the destinations this suite owns, those declared with `Declare`, before the first tap is prepared. Declaration is idempotent: an existing destination is left as it is and a repeated declaration is a no-op. A broker whose destinations always exist (the in-memory broker) implements it as a no-op, and an adapter that cannot create a destination refuses instead of pretending: the default interface implementation throws `NotSupportedException` naming the adapter, so a `Declare` against it fails setup loudly.

## Writing an adapter

An adapter's consumer derives from `ProtoMessageConsumerBase` and supplies only a source; the base owns the await queue the contract above describes:

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

An adapter's public `AwaitAsync` does whatever per-destination work it needs, declaring a tap or resolving the harness, and then calls the protected overload, which resolves the source factory inside the base's await gate. `ProtoMessageAwaitSnapshot` carries the candidates (`ProtoMessageAwaitEntry`: one delivery with its position) plus the wake-up that ends the next wait, when the source has one; the position is the first delivery the consumer may consume, so the in-memory broker starts after the shared history's creation position (`Reset` re-baselines a replaced source) while an empty tap starts at zero. An adapter never implements its own await gate, position, consumed set or deadline rescan.

## Links

- [Messaging](./index.md) - publish, await, destinations, and the trace shape.
- [MassTransit](./masstransit.md) - the harness bridge built on this contract.
