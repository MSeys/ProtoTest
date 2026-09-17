namespace ProtoTest.Core.Internal;

using System.Diagnostics;

internal sealed class ProtoResourceRegistry
{
    private readonly ProtoLock _gate = new();
    private readonly Dictionary<string, Entry> _byId = new(StringComparer.Ordinal);
    private readonly List<string> _registrationOrder = [];
    private int _releaseStarted;

    public void Register(IProtoResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        lock (_gate)
        {
            if (_releaseStarted != 0)
            {
                throw new ObjectDisposedException(
                    nameof(ProtoResourceRegistry),
                    "Resources cannot be registered after the execution context started releasing them.");
            }

            if (_byId.ContainsKey(resource.Id))
            {
                throw new InvalidOperationException(
                    $"A resource with id '{resource.Id}' is already registered in the current ProtoExecutionContext.");
            }

            _registrationOrder.Add(resource.Id);
            _byId[resource.Id] = new Entry(resource);
        }
    }

    public IReadOnlyList<ProtoResourceSnapshot> Snapshot()
    {
        lock (_gate)
        {
            return [.. _registrationOrder.Select(id => _byId[id].ToSnapshot())];
        }
    }

    public async ValueTask<bool> ReleaseAsync(
        string id,
        ProtoExecutionContext test,
        IProtoTraceWriter trace,
        ProtoTracePhase phase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Entry? entry;
        lock (_gate)
        {
            if (!_byId.TryGetValue(id, out entry) || entry.State != ProtoResourceState.Registered)
            {
                return false;
            }
        }

        await ReleaseEntryAsync(entry, test, trace, phase);
        return true;
    }

    public async ValueTask<IReadOnlyList<Exception>> ReleaseAllAsync(
        ProtoExecutionContext test,
        IProtoTraceWriter trace,
        ProtoTracePhase phase)
    {
        if (Interlocked.Exchange(ref _releaseStarted, 1) != 0)
        {
            return [];
        }

        List<Entry> entries;
        lock (_gate)
        {
            entries = [.. _registrationOrder.AsEnumerable().Reverse().Select(id => _byId[id])];
        }

        var exceptions = new List<Exception>();
        foreach (var entry in entries)
        {
            if (entry.State != ProtoResourceState.Registered)
            {
                continue;
            }

            var exception = await ReleaseEntryAsync(entry, test, trace, phase);
            if (exception is not null)
            {
                exceptions.Add(exception);
            }
        }

        return exceptions;
    }

    private static async Task<Exception?> ReleaseEntryAsync(
        Entry entry,
        ProtoExecutionContext test,
        IProtoTraceWriter trace,
        ProtoTracePhase phase)
    {
        var resource = entry.Resource;
        using var operation = trace
            .Operation("resource.release", $"Release · {resource.Id}", "ProtoTest.Core")
            .During(phase)
            .With("resource.id", resource.Id)
            .With("resource.kind", resource.Kind)
            .With("resource.description", resource.Description)
            .Begin();
        var started = Stopwatch.GetTimestamp();
        try
        {
            await resource.ReleaseAsync(new ProtoResourceReleaseContext(test, trace, phase, CancellationToken.None));
            entry.MarkReleased(Stopwatch.GetElapsedTime(started));
            operation.Succeed();
            return null;
        }
        catch (Exception exception)
        {
            entry.MarkFailed(Stopwatch.GetElapsedTime(started), exception);
            operation.Fail(exception);
            return exception;
        }
    }

    private sealed class Entry(IProtoResource resource)
    {
        public IProtoResource Resource { get; } = resource;
        public ProtoResourceState State { get; private set; } = ProtoResourceState.Registered;
        public TimeSpan? ReleaseDuration { get; private set; }
        public string? Error { get; private set; }

        public void MarkReleased(TimeSpan duration)
        {
            State = ProtoResourceState.Released;
            ReleaseDuration = duration;
        }

        public void MarkFailed(TimeSpan duration, Exception exception)
        {
            State = ProtoResourceState.ReleaseFailed;
            ReleaseDuration = duration;
            Error = exception.Message;
        }

        public ProtoResourceSnapshot ToSnapshot()
            => new(Resource.Id, Resource.Kind, Resource.Description, State, ReleaseDuration, Error);
    }
}
