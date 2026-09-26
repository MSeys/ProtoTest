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

        return new ValueTask<IProtoMessageConsumer>(new InMemoryProtoMessageConsumer(this, position));
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

    /// <summary>Returns the first message after <paramref name="position"/> that matches, or null.</summary>
    private MatchedMessage? Find(
        string destination,
        Func<ProtoMessage, bool> predicate,
        long position,
        HashSet<long> consumed)
    {
        Entry[] candidates;
        lock (_gate)
        {
            candidates = [.. _messages.Where(entry => entry.Position > position)];
        }

        foreach (var entry in candidates)
        {
            if (consumed.Contains(entry.Position))
            {
                continue;
            }

            if (!string.Equals(entry.Message.Destination, destination, StringComparison.Ordinal))
            {
                continue;
            }

            if (predicate(entry.Message))
            {
                return new MatchedMessage(entry.Message, entry.Position);
            }
        }

        return null;
    }

    private static TaskCompletionSource NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly record struct Entry(long Position, ProtoMessage Message);

    private readonly record struct MatchedMessage(ProtoMessage Message, long Position);

    private sealed class InMemoryProtoMessageConsumer(InMemoryProtoMessageBroker broker, long afterPosition)
        : IProtoMessageConsumer
    {
        // One consumer serves one await at a time, in call order, and a matched message is consumed
        // exactly once. A delivery that matched no awaited predicate stays in the history, so a later
        // await on this consumer can still match it and two concurrent awaits cannot steal each
        // other's messages. Both fields are only touched while _awaitGate is held.
        private readonly SemaphoreSlim _awaitGate = new(1, 1);
        private readonly HashSet<long> _consumed = [];
        private readonly long _position = afterPosition;

        public ValueTask PrepareAsync(
            IReadOnlyCollection<string> destinations,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(destinations);
            return ValueTask.CompletedTask;
        }

        public async ValueTask<ProtoMessage> AwaitAsync(
            string destination,
            Func<ProtoMessage, bool> predicate,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            await _awaitGate.WaitAsync(cancellationToken);
            try
            {
                var matched = await AwaitMatchAsync(
                    destination,
                    predicate,
                    timeout,
                    _position,
                    cancellationToken);
                // A match is consumed: a later await on this consumer never matches it again, exactly
                // like an auto-acking RabbitMQ tap.
                _consumed.Add(matched.Position);
                return matched.Message;
            }
            finally
            {
                _awaitGate.Release();
            }
        }

        private async ValueTask<MatchedMessage> AwaitMatchAsync(
            string destination,
            Func<ProtoMessage, bool> predicate,
            TimeSpan timeout,
            long position,
            CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                Task signal;
                lock (broker._gate)
                {
                    // The signal is captured before the scan: a publish that lands in between completes
                    // this signal, so the wait cannot miss it, and one that landed before it is already
                    // in the history the scan reads.
                    signal = broker._published.Task;
                }

                if (broker.Find(destination, predicate, position, _consumed) is { } matched)
                {
                    return matched;
                }

                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    // A match assigned at the same instant the deadline passes must win, never time out.
                    if (broker.Find(destination, predicate, position, _consumed) is { } lateMatch)
                    {
                        return lateMatch;
                    }

                    throw new TimeoutException(
                        $"No message matching the predicate arrived on '{destination}' within {timeout.TotalSeconds:0.###}s.");
                }

                var timeoutTask = Task.Delay(remaining, CancellationToken.None);
                var cancellation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var registration = cancellationToken.Register(() => cancellation.TrySetResult());
                await Task.WhenAny(signal, timeoutTask, cancellation.Task);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
