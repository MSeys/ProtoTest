namespace ProtoTest.Messaging.Internal;

/// <summary>
/// The default broker: messages live for the run, ordered by a publish position, and every consumer
/// snapshots the current position at creation, so it only ever matches messages published after its own
/// test started. A matched message is consumed and never matched again; a delivery that matched no
/// awaited predicate stays in the history for a later await, so concurrent awaits on one consumer
/// neither lose nor steal each other's messages. One lock guards the history and the signal; predicates
/// always run in the awaiting flow, so a slow or throwing predicate cannot stall publishers.
/// It makes the API and the demo independent of infrastructure; a real adapter replaces it with the
/// broker the system under test actually uses.
/// </summary>
internal sealed class InMemoryProtoMessageBroker : IProtoMessageBroker
{
    private readonly ProtoLock _gate = new();
    private readonly List<Entry> _messages = [];
    private TaskCompletionSource _published = NewSignal();
    private long _position;

    public string Name => "InMemory";

    public ValueTask<IProtoMessageConsumer> CreateConsumerAsync(CancellationToken cancellationToken = default)
    {
        long position;
        lock (_gate)
        {
            position = _position;
        }

        // The consumer may match from the next position on: everything published before it belongs to
        // an earlier test.
        return new ValueTask<IProtoMessageConsumer>(new InMemoryProtoMessageConsumer(this, position + 1));
    }

    public ValueTask PublishAsync(ProtoMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        TaskCompletionSource signal;
        lock (_gate)
        {
            _position++;
            _messages.Add(new Entry(_position, message));
            signal = _published;
            _published = NewSignal();
        }

        // Completed outside the lock: a waiter woken by this signal scans the history the lock protects.
        signal.TrySetResult();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// A declaration is a no-op: the in-memory broker has no topology to create, and every destination
    /// already exists - publishing creates its history entry and awaiting reads it.
    /// </summary>
    public ValueTask DeclareAsync(
        IReadOnlyCollection<string> destinations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destinations);
        return ValueTask.CompletedTask;
    }

    private static TaskCompletionSource NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly record struct Entry(long Position, ProtoMessage Message);

    /// <summary>
    /// One test's consumer over the broker's shared history: the base owns the await queue, the source
    /// is the broker's history, so the consumer itself carries no await state. A queue destination is
    /// refused: the in-memory broker has no queues, so awaiting one names the transport and the remedy
    /// instead of matching against a destination that only looks like a queue.
    /// </summary>
    private sealed class InMemoryProtoMessageConsumer(InMemoryProtoMessageBroker broker, long position)
        : ProtoMessageConsumerBase(position)
    {
        private const string QueueRemedy =
            "A queue destination is served by a broker that owns queues (the RabbitMQ adapter); " +
            "await the exchange that feeds the queue instead.";

        public override ValueTask PrepareAsync(
            IReadOnlyCollection<string> destinations,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(destinations);
            foreach (var destination in destinations)
            {
                if (ProtoDestination.IsQueue(destination))
                {
                    throw ProtoDestination.QueueUnsupported(destination, broker.Name, QueueRemedy);
                }
            }

            return ValueTask.CompletedTask;
        }

        public override ValueTask<ProtoMessage> AwaitAsync(
            string destination,
            Func<ProtoMessage, bool> predicate,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            if (ProtoDestination.IsQueue(destination))
            {
                throw ProtoDestination.QueueUnsupported(destination, broker.Name, QueueRemedy);
            }

            return AwaitAsync(destination, predicate, timeout, cancellationToken, () => new Source(broker));
        }

        /// <summary>
        /// The broker's history as an await source: a snapshot copies the messages at or after the
        /// awaited position and carries the publish signal captured before the copy, so a publish that
        /// lands during the scan completes the next wait instead of waiting for the deadline.
        /// </summary>
        private sealed class Source(InMemoryProtoMessageBroker broker) : IProtoMessageAwaitSource
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
                    // The signal is captured before the copy: a publish that lands in between completes
                    // this signal, so the wait cannot miss it, and one that landed before it is already
                    // in the history the copy reads.
                    changed = broker._published.Task;
                    foreach (var entry in broker._messages)
                    {
                        if (entry.Position >= position &&
                            string.Equals(entry.Message.Destination, destination, StringComparison.Ordinal))
                        {
                            candidates.Add(new ProtoMessageAwaitEntry(entry.Position, entry.Message));
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
