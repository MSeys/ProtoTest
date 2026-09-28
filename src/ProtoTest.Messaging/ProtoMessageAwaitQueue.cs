namespace ProtoTest.Messaging;

using System.Diagnostics;

/// <summary>
/// One consumer's await machinery, shared by the in-memory, RabbitMQ and MassTransit adapters: awaits
/// on the consumer serialize in call order, only messages at or after the consumer's position can
/// match, a matched message is consumed exactly once, and a message that matched no awaited predicate
/// stays in the source for a later await. The scan runs before every wait and again when the wait
/// returns at the deadline, so a delivery assigned at the same instant as the timeout still wins.
/// An adapter contributes an <see cref="IProtoMessageAwaitSource"/> over its own storage and owns one
/// queue per consumer; it never implements its own await loop, gate, position or consumed set.
/// </summary>
/// <param name="position">
/// The first position an await may consume; a source's older messages can never match.
/// </param>
public sealed class ProtoMessageAwaitQueue(long position)
{
    private readonly SemaphoreSlim _awaitGate = new(1, 1);
    private readonly ProtoLock _stateGate = new();
    private readonly HashSet<long> _consumed = [];
    private long _position = position;

    /// <summary>
    /// Re-baselines the queue: <paramref name="position"/> replaces the current position and the
    /// consumed set is dropped. A consumer whose source is replaced mid-run - the MassTransit bridge
    /// when a substitution gives the test its own harness, whose history starts empty - resets before
    /// it scans the new source.
    /// </summary>
    public void Reset(long position)
    {
        lock (_stateGate)
        {
            _position = position;
            _consumed.Clear();
        }
    }

    /// <summary>
    /// Waits for the first message on <paramref name="destination"/> that matches
    /// <paramref name="predicate"/> within <paramref name="timeout"/>. The source is resolved inside
    /// the await gate, so a consumer that re-baselines while awaits are already queued cannot race an
    /// in-flight scan.
    /// </summary>
    /// <exception cref="TimeoutException">No message matched within the timeout.</exception>
    public async ValueTask<ProtoMessage> AwaitAsync(
        string destination,
        Func<ProtoMessage, bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        Func<IProtoMessageAwaitSource> sourceFactory)
    {
        ArgumentNullException.ThrowIfNull(sourceFactory);
        await _awaitGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var source = sourceFactory();
            var stopwatch = Stopwatch.StartNew();
            while (true)
            {
                var snapshot = await source.SnapshotAsync(destination, Position(), cancellationToken).ConfigureAwait(false);
                if (Find(snapshot.Candidates, predicate) is { } matched)
                {
                    return matched;
                }

                var remaining = timeout - stopwatch.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    // The scan above ran after the wait returned, so a delivery that landed at the
                    // deadline already had its chance; nothing can match before this await fails.
                    throw Timeout(destination, timeout);
                }

                await source.WaitAsync(snapshot, remaining, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _awaitGate.Release();
        }
    }

    /// <summary>
    /// Returns the first candidate at or after the position that no await consumed yet and that the
    /// predicate accepts, marking it consumed; null when none matches. The predicate runs in the
    /// awaiting flow, so a slow or throwing predicate fails only the await that owns it.
    /// </summary>
    private ProtoMessage? Find(IReadOnlyList<ProtoMessageAwaitEntry> candidates, Func<ProtoMessage, bool> predicate)
    {
        foreach (var entry in candidates)
        {
            lock (_stateGate)
            {
                if (entry.Position < _position || _consumed.Contains(entry.Position))
                {
                    continue;
                }
            }

            if (predicate(entry.Message))
            {
                lock (_stateGate)
                {
                    _consumed.Add(entry.Position);
                }

                return entry.Message;
            }
        }

        return null;
    }

    private long Position()
    {
        lock (_stateGate)
        {
            return _position;
        }
    }

    private static TimeoutException Timeout(string destination, TimeSpan timeout)
        => new($"No message matching the predicate arrived on '{destination}' within {timeout.TotalSeconds:0.###}s.");
}
