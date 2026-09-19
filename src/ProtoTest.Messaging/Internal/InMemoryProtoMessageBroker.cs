namespace ProtoTest.Messaging.Internal;

/// <summary>
/// The default broker: messages live for the run, ordered by a publish position, and every consumer
/// snapshots the current position at creation, so it only ever matches messages published after its own
/// test started. A match advances that consumer's position, so repeated awaits consume the stream like
/// RabbitMQ does instead of re-delivering the first match. It makes the API and the demo independent of
/// infrastructure; a real adapter replaces it with the broker the system under test actually uses.
/// </summary>
internal sealed class InMemoryProtoMessageBroker : IProtoMessageBroker
{
    private readonly ProtoLock _gate = new();
    private readonly List<Entry> _messages = [];
    private readonly List<Waiter> _waiters = [];
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
        long position;
        Waiter[] waiters;
        lock (_gate)
        {
            position = ++_position;
            _messages.Add(new Entry(position, message));
            waiters = [.. _waiters];
        }

        // User predicates run outside the broker lock: a slow or throwing predicate must not stall other
        // publishes, and its failure only fails the await that owns the predicate.
        foreach (var waiter in waiters)
        {
            if (position <= waiter.AfterPosition
                || !string.Equals(message.Destination, waiter.Destination, StringComparison.Ordinal))
            {
                continue;
            }

            lock (waiter)
            {
                bool matched;
                try
                {
                    matched = waiter.Predicate(message);
                }
                catch (Exception exception)
                {
                    lock (_gate)
                    {
                        _waiters.Remove(waiter);
                    }

                    waiter.Completion.TrySetException(exception);
                    continue;
                }

                if (!matched) continue;
                lock (_gate)
                {
                    _waiters.Remove(waiter);
                    // Completed under the lock: an await whose timeout fires at the same instant either
                    // sees the completed match or the waiter is already gone and the timeout wins; it can
                    // never time out after a match was assigned.
                    waiter.Completion.TrySetResult(new MatchedMessage(message, position));
                }
            }
        }

        return ValueTask.CompletedTask;
    }

    private async ValueTask<MatchedMessage> AwaitAsync(
        string destination,
        Func<ProtoMessage, bool> predicate,
        TimeSpan timeout,
        long afterPosition,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(predicate);
        Task<MatchedMessage> completion;
        Waiter waiter;
        lock (_gate)
        {
            var existing = _messages
                .Where(entry => entry.Position > afterPosition
                    && string.Equals(entry.Message.Destination, destination, StringComparison.Ordinal)
                    && predicate(entry.Message))
                .Cast<Entry?>()
                .FirstOrDefault();
            if (existing is { } found)
            {
                return new MatchedMessage(found.Message, found.Position);
            }

            waiter = new Waiter(destination, predicate, afterPosition);
            _waiters.Add(waiter);
            completion = waiter.Completion.Task;
        }

        var timeoutTask = Task.Delay(timeout, CancellationToken.None);
        var cancellation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() => cancellation.TrySetResult());
        await Task.WhenAny(completion, timeoutTask, cancellation.Task);

        bool assigned;
        lock (_gate)
        {
            _waiters.Remove(waiter);
            assigned = completion.IsCompleted;
        }

        if (assigned)
        {
            return await completion;
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new TimeoutException(
            $"No message matching the predicate arrived on '{destination}' within {timeout.TotalSeconds:0.###}s.");
    }

    private readonly record struct Entry(long Position, ProtoMessage Message);

    private readonly record struct MatchedMessage(ProtoMessage Message, long Position);

    private sealed class InMemoryProtoMessageConsumer(InMemoryProtoMessageBroker broker, long afterPosition)
        : IProtoMessageConsumer
    {
        // One consumer delivers each message once: concurrent awaits serialize, so the later one never
        // snapshots the position the earlier one is about to consume past. Without the gate both waiters
        // match the same publish and the second message stays unread.
        private readonly SemaphoreSlim _awaitGate = new(1, 1);
        private long _position = afterPosition;

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
                var matched = await broker.AwaitAsync(
                    destination,
                    predicate,
                    timeout,
                    Volatile.Read(ref _position),
                    cancellationToken);
                // A match is consumed: advance past it so a later await sees the next message, exactly
                // like an auto-acking RabbitMQ tap.
                Volatile.Write(ref _position, matched.Position);
                return matched.Message;
            }
            finally
            {
                _awaitGate.Release();
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Waiter(string destination, Func<ProtoMessage, bool> predicate, long afterPosition)
    {
        public string Destination { get; } = destination;

        public Func<ProtoMessage, bool> Predicate { get; } = predicate;

        public long AfterPosition { get; } = afterPosition;

        public TaskCompletionSource<MatchedMessage> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
