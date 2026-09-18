namespace ProtoTest.Messaging.Internal;

/// <summary>
/// The default broker: messages live for the run, ordered by a publish position, and
/// <see cref="AwaitAsync"/> resolves immediately when a matching message already arrived after the
/// consumer's position. It makes the API and the demo independent of infrastructure; a real adapter
/// replaces it with the broker the system under test actually uses.
/// </summary>
internal sealed class InMemoryProtoMessageBroker : IProtoMessageBroker
{
    private readonly ProtoLock _gate = new();
    private readonly List<Entry> _messages = [];
    private readonly List<Waiter> _waiters = [];
    private long _position;

    public string Name => "InMemory";

    public long Position
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
        List<Waiter>? matched = null;
        lock (_gate)
        {
            var entry = new Entry(++_position, message);
            _messages.Add(entry);
            for (var index = _waiters.Count - 1; index >= 0; index--)
            {
                var waiter = _waiters[index];
                if (entry.Position <= waiter.AfterPosition
                    || !string.Equals(message.Destination, waiter.Destination, StringComparison.Ordinal)
                    || !waiter.Predicate(message))
                {
                    continue;
                }

                matched ??= [];
                matched.Add(waiter);
                _waiters.RemoveAt(index);
            }
        }

        foreach (var waiter in matched ?? [])
        {
            waiter.Completion.TrySetResult(message);
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask<ProtoMessage> AwaitAsync(
        string destination,
        Func<ProtoMessage, bool> predicate,
        TimeSpan timeout,
        long afterPosition,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(predicate);
        Task<ProtoMessage> completion;
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
                return found.Message;
            }

            waiter = new Waiter(destination, predicate, afterPosition);
            _waiters.Add(waiter);
            completion = waiter.Completion.Task;
        }

        var timeoutTask = Task.Delay(timeout, CancellationToken.None);
        var cancellation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() => cancellation.TrySetResult());
        var completed = await Task.WhenAny(completion, timeoutTask, cancellation.Task);

        lock (_gate)
        {
            _waiters.Remove(waiter);
        }

        // A publish may have matched the waiter between WhenAny's decision and its removal.
        if (completion.IsCompleted)
        {
            return await completion;
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new TimeoutException(
            $"No message matching the predicate arrived on '{destination}' within {timeout.TotalSeconds:0.###}s.");
    }

    private readonly record struct Entry(long Position, ProtoMessage Message);

    private sealed class Waiter(string destination, Func<ProtoMessage, bool> predicate, long afterPosition)
    {
        public string Destination { get; } = destination;

        public Func<ProtoMessage, bool> Predicate { get; } = predicate;

        public long AfterPosition { get; } = afterPosition;

        public TaskCompletionSource<ProtoMessage> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
