namespace ProtoTest.Messaging;

using ProtoTest.Messaging.Internal;

/// <summary>
/// The base every messaging adapter's consumer derives from. It owns the one await queue: waits on a
/// consumer serialize in call order, only deliveries at or after the consumer's position can match, a
/// matched delivery is consumed exactly once, a delivery that matched no awaited predicate stays for a
/// later await, and the source is scanned once more when the wait returns at the deadline, so a
/// delivery assigned at the same instant as the timeout still wins. An adapter implements
/// <see cref="IProtoMessageConsumer.AwaitAsync"/> by resolving its <see cref="IProtoMessageAwaitSource"/>
/// and calling the protected <c>AwaitAsync(…, sourceFactory)</c> overload: it supplies a source
/// (snapshot + wait), never its own await loop, gate, position or consumed set.
/// </summary>
public abstract class ProtoMessageConsumerBase : IProtoMessageConsumer
{
    private readonly ProtoMessageAwaitQueue _queue;

    /// <summary>
    /// Creates the consumer with the first position an await may consume. The queue never matches a
    /// source entry before it, so a source the consumer shares with other tests can keep its history.
    /// </summary>
    /// <param name="position">The first position an await may consume.</param>
    protected ProtoMessageConsumerBase(long position)
    {
        _queue = new ProtoMessageAwaitQueue(position);
    }

    /// <summary>
    /// The default preparation: a no-op, because the adapter's destinations already exist or its
    /// history covers them. An adapter that declares per-test destinations overrides this.
    /// </summary>
    public virtual ValueTask PrepareAsync(
        IReadOnlyCollection<string> destinations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destinations);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public abstract ValueTask<ProtoMessage> AwaitAsync(
        string destination,
        Func<ProtoMessage, bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The default disposal: a no-op, because the adapter owns nothing outside the consumer. An
    /// adapter that declared destinations for the test overrides this to remove them.
    /// </summary>
    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// Waits through the one await queue: the source factory is resolved inside the queue's await
    /// gate, so a consumer that re-baselines while awaits are already queued cannot race an in-flight
    /// scan. An adapter's public await resolves its source here.
    /// </summary>
    /// <param name="destination">The destination the predicate matches on.</param>
    /// <param name="predicate">The match the first message at or after the position must satisfy.</param>
    /// <param name="timeout">How long the await may take before it fails.</param>
    /// <param name="cancellationToken">Cancels the wait and releases the queue.</param>
    /// <param name="sourceFactory">Resolves the adapter's source for this await.</param>
    /// <exception cref="TimeoutException">No message matched within <paramref name="timeout"/>.</exception>
    protected ValueTask<ProtoMessage> AwaitAsync(
        string destination,
        Func<ProtoMessage, bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        Func<IProtoMessageAwaitSource> sourceFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(sourceFactory);
        return _queue.AwaitAsync(destination, predicate, timeout, cancellationToken, sourceFactory);
    }

    /// <summary>
    /// Re-baselines the queue: <paramref name="position"/> replaces the current position and the
    /// consumed set is dropped. A consumer whose source is replaced mid-run resets before it scans the
    /// new source, whose positions are unrelated to the old one's.
    /// </summary>
    /// <param name="position">The first position an await may consume from the new source.</param>
    protected void Reset(long position) => _queue.Reset(position);
}
