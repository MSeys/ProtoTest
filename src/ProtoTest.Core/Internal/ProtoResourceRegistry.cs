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

    public IReadOnlyList<IProtoResource> Resources
    {
        get
        {
            lock (_gate)
            {
                return [.. _registrationOrder.Select(id => _byId[id].Resource)];
            }
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
        ProtoExecutionContext? test,
        IProtoTraceWriter trace,
        ProtoTracePhase phase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Entry? entry;
        lock (_gate)
        {
            if (!_byId.TryGetValue(id, out entry) || !entry.TryBeginRelease())
            {
                return false;
            }
        }

        await ReleaseEntryAsync(entry, test, trace, phase);
        return true;
    }

    public async ValueTask<IReadOnlyList<Exception>> ReleaseAllAsync(
        ProtoExecutionContext? test,
        IProtoTraceWriter trace,
        ProtoTracePhase phase)
    {
        List<Entry> entries;
        lock (_gate)
        {
            if (_releaseStarted != 0)
            {
                return [];
            }

            // Flipping the gate and taking the snapshot under the same lock means a racing Register
            // either sees the gate closed or lands in the snapshot, never in neither.
            _releaseStarted = 1;
            entries = [.. _registrationOrder.AsEnumerable().Reverse().Select(id => _byId[id])];
        }

        var exceptions = new List<Exception>();
        foreach (var entry in entries)
        {
            if (!entry.TryBeginRelease())
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

    /// <summary>
    /// Reopens the one-shot release gate after a failed run start released what had started, so a retry
    /// can attempt the release that belongs to the run's real end. Entries that were already released or
    /// failed keep their state: a resource is released at most once, and a retry must not release it again.
    /// </summary>
    public void ResetForRestart()
    {
        lock (_gate)
        {
            _releaseStarted = 0;
        }
    }

    private static async Task<Exception?> ReleaseEntryAsync(
        Entry entry,
        ProtoExecutionContext? test,
        IProtoTraceWriter trace,
        ProtoTracePhase phase)
    {
        var resource = entry.Resource;
        // A client is framework plumbing: its life is on the client entity, not a lifecycle step. A
        // failed release still produces an entry, because a failing teardown must be explainable.
        var frameworkManaged = string.Equals(resource.Kind, "client", StringComparison.OrdinalIgnoreCase);
        using var operation = frameworkManaged
            ? null
            : trace
                .Operation("resource.release", $"Release · {resource.Id}", "ProtoTest.Core")
                .During(phase)
                .For(resource.Kind, resource.Id)
                .With("resource.id", resource.Id)
                .With("resource.kind", resource.Kind)
                .With("resource.description", resource.Description)
                .Begin();
        var started = Stopwatch.GetTimestamp();
        try
        {
            await resource.ReleaseAsync(new ProtoResourceReleaseContext(test, trace, phase, CancellationToken.None));
            var elapsed = Stopwatch.GetElapsedTime(started);
            entry.MarkReleased(elapsed);
            operation?.Succeed();
            trace.SetEntityState(
                resource.Kind,
                resource.Id,
                $"Resource · {resource.Id}",
                new Dictionary<string, string?>
                {
                    ["resource.state"] = "released",
                    ["resource.release_ms"] = Math.Round(elapsed.TotalMilliseconds, 3).ToString(
                        System.Globalization.CultureInfo.InvariantCulture)
                },
                change: "released");
            return null;
        }
        catch (Exception exception)
        {
            entry.MarkFailed(Stopwatch.GetElapsedTime(started), exception);
            operation?.Fail(exception);
            if (frameworkManaged)
            {
                trace.WriteEvent(
                    "resource.release",
                    $"Release · {resource.Id}",
                    "ProtoTest.Core",
                    phase,
                    ProtoTraceOutcome.Failed,
                    new Dictionary<string, string?>
                    {
                        ["resource.id"] = resource.Id,
                        ["resource.kind"] = resource.Kind,
                        ["resource.description"] = resource.Description
                    },
                    exception,
                    entityKind: resource.Kind,
                    entityId: resource.Id);
            }

            trace.SetEntityState(
                resource.Kind,
                resource.Id,
                $"Resource · {resource.Id}",
                new Dictionary<string, string?>
                {
                    ["resource.state"] = "release_failed",
                    ["resource.error"] = exception.Message
                },
                change: "failed");
            return exception;
        }
    }

    private sealed class Entry(IProtoResource resource)
    {
        private readonly ProtoLock _releaseGate = new();
        private ProtoResourceState _state = ProtoResourceState.Registered;
        private TimeSpan? _releaseDuration;
        private string? _error;
        private bool _releasing;

        public IProtoResource Resource { get; } = resource;

        public ProtoResourceState State
        {
            get
            {
                lock (_releaseGate)
                {
                    return _state;
                }
            }
        }

        /// <summary>
        /// Claims the one release this entry permits. The transition happens under the entry's lock, so
        /// two concurrent callers cannot both run the resource's release callback.
        /// </summary>
        public bool TryBeginRelease()
        {
            lock (_releaseGate)
            {
                if (_state != ProtoResourceState.Registered || _releasing)
                {
                    return false;
                }

                _releasing = true;
                return true;
            }
        }

        public void MarkReleased(TimeSpan duration)
        {
            lock (_releaseGate)
            {
                _state = ProtoResourceState.Released;
                _releaseDuration = duration;
            }
        }

        public void MarkFailed(TimeSpan duration, Exception exception)
        {
            lock (_releaseGate)
            {
                _state = ProtoResourceState.ReleaseFailed;
                _releaseDuration = duration;
                _error = exception.Message;
            }
        }

        public ProtoResourceSnapshot ToSnapshot()
        {
            lock (_releaseGate)
            {
                return new ProtoResourceSnapshot(Resource.Id, Resource.Kind, Resource.Description, _state, _releaseDuration, _error);
            }
        }
    }
}
