namespace ProtoTest.Extensibility.Tests;

using ProtoTest.Messaging;

/// <summary>
/// A minimal broker adapter written against the public messaging surface only - this project has no
/// InternalsVisibleTo grant, so it compiles exactly like a third-party adapter package. The broker
/// keeps a publish-ordered history for the run and creates one consumer per test from the current
/// position, so a consumer only matches messages published after its own test started. The consumer
/// derives from <see cref="ProtoMessageConsumerBase"/> and supplies only an
/// <see cref="IProtoMessageAwaitSource"/> (a snapshot and a wake-up), which is the documented adapter
/// recipe: the base owns serialization, position, consumption and the deadline rescan.
/// </summary>
public sealed class MinimalBroker : IProtoMessageBroker
{
    private readonly ProtoLock _gate = new();
    private readonly List<ProtoMessageAwaitEntry> _history = [];
    private TaskCompletionSource _published = NewSignal();
    private long _position;

    /// <summary>The adapter name recorded on the capability and the broker entity.</summary>
    public string Name => "Minimal";

    /// <summary>How many messages this broker published for the run.</summary>
    public long PublishedCount
    {
        get
        {
            lock (_gate)
            {
                return _position;
            }
        }
    }

    public ValueTask PublishAsync(ProtoMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        TaskCompletionSource signal;
        lock (_gate)
        {
            _position++;
            _history.Add(new ProtoMessageAwaitEntry(_position, message));
            signal = _published;
            _published = NewSignal();
        }

        // Completed outside the lock: a waiter woken by this signal scans the history the lock protects.
        signal.TrySetResult();
        return ValueTask.CompletedTask;
    }

    public ValueTask<IProtoMessageConsumer> CreateConsumerAsync(CancellationToken cancellationToken = default)
    {
        long position;
        lock (_gate)
        {
            position = _position;
        }

        // The consumer may match from the next position on: everything published before it belongs to
        // an earlier test.
        return new ValueTask<IProtoMessageConsumer>(new MinimalConsumer(this, position + 1));
    }

    /// <summary>Every destination exists in memory, so a declaration is a no-op.</summary>
    public ValueTask DeclareAsync(
        IReadOnlyCollection<string> destinations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destinations);
        return ValueTask.CompletedTask;
    }

    private static TaskCompletionSource NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class MinimalConsumer(MinimalBroker broker, long position) : ProtoMessageConsumerBase(position)
    {
        public override ValueTask<ProtoMessage> AwaitAsync(
            string destination,
            Func<ProtoMessage, bool> predicate,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
            => AwaitAsync(destination, predicate, timeout, cancellationToken, () => new Source(broker));

        /// <summary>
        /// The broker's history as an await source: a snapshot copies the messages at or after the
        /// awaited position and carries the publish signal captured before the copy, so a publish that
        /// lands during the scan completes the next wait instead of waiting for the deadline.
        /// </summary>
        private sealed class Source(MinimalBroker broker) : IProtoMessageAwaitSource
        {
            public ValueTask<ProtoMessageAwaitSnapshot> SnapshotAsync(
                string destination,
                long position,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Task changed;
                List<ProtoMessageAwaitEntry> candidates = [];
                lock (broker._gate)
                {
                    changed = broker._published.Task;
                    foreach (var entry in broker._history)
                    {
                        if (entry.Position >= position &&
                            string.Equals(entry.Message.Destination, destination, StringComparison.Ordinal))
                        {
                            candidates.Add(entry);
                        }
                    }
                }

                return new(new ProtoMessageAwaitSnapshot(candidates, changed));
            }

            public async ValueTask WaitAsync(
                ProtoMessageAwaitSnapshot snapshot,
                TimeSpan remaining,
                CancellationToken cancellationToken)
            {
                var delay = Task.Delay(remaining, CancellationToken.None);
                var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var registration = cancellationToken.Register(() => cancelled.TrySetResult());
                await Task.WhenAny(snapshot.Changed ?? delay, delay, cancelled.Task).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }
}
