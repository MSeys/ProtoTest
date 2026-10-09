namespace ProtoTest.Core.Internal;

using System.Diagnostics;
using System.Runtime.ExceptionServices;

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

    /// <summary>
    /// Forgets a resource that will not be used, so it is neither owned nor released. Only valid
    /// before any release started, which is where the host drops infrastructure the environment
    /// already provides.
    /// </summary>
    public bool TryRemove(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        lock (_gate)
        {
            if (_releaseStarted != 0 || !_byId.Remove(id))
            {
                return false;
            }

            _registrationOrder.Remove(id);
            return true;
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
            if (!_byId.TryGetValue(id, out entry) || !entry.TryBeginRelease(out _))
            {
                return false;
            }
        }

        // The failure is recorded before it is thrown, and left on the entry so the later central
        // release can count it. A caller that did not start this release gets false instead.
        var exception = await ReleaseEntryAsync(entry, test, trace, phase);
        if (exception is not null)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

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
            if (!entry.TryBeginRelease(out var inFlight))
            {
                // Started is not completed: an early release may still be inside its callback, and
                // that callback can still need the scope this release is about to drop. A failure
                // already recorded is taken once, so a restart does not report it again.
                if (inFlight is not null)
                {
                    await inFlight;
                }

                var earlier = entry.ConsumeFailure();
                if (earlier is not null)
                {
                    exceptions.Add(earlier);
                }

                continue;
            }

            var exception = await ReleaseEntryAsync(entry, test, trace, phase);
            // This call ran the callback, so the stored failure is already in `exception`. Taking it
            // here means a restarted run does not report that same failure a second time.
            _ = entry.ConsumeFailure();
            if (exception is not null)
            {
                exceptions.Add(exception);
            }
        }

        return exceptions;
    }

    /// <summary>
    /// Reopens the one-shot release gate after a failed run start released what had started, so a retry
    /// can attempt the release that belongs to the run's real end. Entries keep their state until the
    /// retry actually starts them again (see <see cref="Rearm"/>), so a resource the retry never
    /// restarts is not released a second time.
    /// </summary>
    public void ResetForRestart()
    {
        lock (_gate)
        {
            _releaseStarted = 0;
        }
    }

    /// <summary>
    /// Re-arms a resource the failed start released, so the retry that starts it again releases it
    /// again at the run's real end. A resource that stays released is not re-released, which is what a
    /// retry that never restarts it needs.
    /// </summary>
    public void Rearm(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        lock (_gate)
        {
            if (_byId.TryGetValue(id, out var entry))
            {
                entry.Rearm();
            }
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
                .Operation("resource.release", $"Release · {resource.Id}", ProtoCoreDiagnostics.TraceSource)
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
                    ProtoCoreDiagnostics.TraceSource,
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
        finally
        {
            entry.SignalComplete();
        }
    }

    private sealed class Entry(IProtoResource resource)
    {
        private readonly ProtoLock _releaseGate = new();
        private ProtoResourceState _state = ProtoResourceState.Registered;
        private TimeSpan? _releaseDuration;
        private string? _error;
        private Exception? _failure;
        private Task? _release;
        private TaskCompletionSource? _completed;

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
        /// two concurrent callers cannot both run the resource's release callback and a snapshot taken
        /// while the callback runs reports <see cref="ProtoResourceState.Releasing"/>.
        /// <paramref name="inFlight"/> is that shared release when this caller did not start it:
        /// started and completed are different, and a later dispose awaits completion.
        /// </summary>
        public bool TryBeginRelease(out Task? inFlight)
        {
            lock (_releaseGate)
            {
                if (_state != ProtoResourceState.Registered)
                {
                    inFlight = _release;
                    return false;
                }

                _state = ProtoResourceState.Releasing;
                var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _completed = completed;
                _release = completed.Task;
                inFlight = _release;
                return true;
            }
        }

        /// <summary>
        /// Unblocks whoever is waiting for this release. The result stays a successful task; the
        /// failure itself is stored on the entry so a waiter can continue with the resources after it.
        /// </summary>
        public void SignalComplete()
        {
            TaskCompletionSource? completed;
            lock (_releaseGate)
            {
                completed = _completed;
            }

            completed?.TrySetResult();
        }

        /// <summary>
        /// Takes the failure recorded for this release, once. The call that ran the callback leaves it
        /// stored when it is an early release; the central release takes it so dispose counts it, and
        /// a later restart does not report the same failure again.
        /// </summary>
        public Exception? ConsumeFailure()
        {
            lock (_releaseGate)
            {
                var failure = _failure;
                _failure = null;
                return failure;
            }
        }

        public void MarkReleased(TimeSpan duration)
        {
            lock (_releaseGate)
            {
                _state = ProtoResourceState.Released;
                _releaseDuration = duration;
                _failure = null;
            }
        }

        /// <summary>
        /// Returns an entry to its registered state for its next release. A failed release is re-armed
        /// too: the host may start the resource again, and that new ownership period must be released
        /// even though the previous one could not be.
        /// </summary>
        public void Rearm()
        {
            lock (_releaseGate)
            {
                if (_state is not (ProtoResourceState.Released or ProtoResourceState.ReleaseFailed))
                {
                    return;
                }

                _state = ProtoResourceState.Registered;
                _releaseDuration = null;
                _error = null;
                _failure = null;
                _release = null;
                _completed = null;
            }
        }

        public void MarkFailed(TimeSpan duration, Exception exception)
        {
            lock (_releaseGate)
            {
                _state = ProtoResourceState.ReleaseFailed;
                _releaseDuration = duration;
                _error = exception.Message;
                _failure = exception;
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
