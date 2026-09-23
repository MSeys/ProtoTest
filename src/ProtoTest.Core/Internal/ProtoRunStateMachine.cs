namespace ProtoTest.Core.Internal;

using System.Runtime.ExceptionServices;

/// <summary>
/// The run's single state machine. Created can start; Starting rejects a second start, a stop and a
/// disposal; Started is the only state a stop tears down; Stopped keeps a failed stop's failure for
/// the retry that asks again; Disposed is final.
/// </summary>
internal sealed class ProtoRunStateMachine
{
    private readonly ProtoLock _gate = new();
    private RunState _state = RunState.Created;
    private Exception? _stopFailure;

    /// <summary>
    /// Claims the start transition. A repeat call after a successful start returns
    /// <see langword="false"/> so the caller skips the start path entirely; every other state rejects
    /// the start, so a start while a start is in flight never records anything twice.
    /// </summary>
    public bool BeginStart()
    {
        lock (_gate)
        {
            switch (_state)
            {
                case RunState.Started:
                    return false;
                case RunState.Starting:
                    throw new InvalidOperationException(ProtoHostGuards.StartupInProgress);
                case RunState.Stopping:
                    throw new InvalidOperationException(
                        "ProtoHost shutdown is in progress; start it after the stop completes.");
                case RunState.Stopped:
                case RunState.Disposed:
                    throw new InvalidOperationException("A ProtoHost cannot be started after it has stopped.");
                default:
                    _state = RunState.Starting;
                    return true;
            }
        }
    }

    /// <summary>Records that the start path finished.</summary>
    public void CompleteStart()
    {
        lock (_gate)
        {
            _state = RunState.Started;
        }
    }

    /// <summary>Returns to Created after a failed start, so a retry is possible.</summary>
    public void RollbackStart()
    {
        lock (_gate)
        {
            _state = RunState.Created;
        }
    }

    /// <summary>
    /// Claims the stop transition. A stop of a stopped run is a no-op unless the earlier stop failed,
    /// in which case the remembered failure is rethrown: the hooks were attempted once and the caller
    /// must still see why they did not finish. A stop rejected because a start is in flight changes
    /// nothing.
    /// </summary>
    public void BeginStop()
    {
        lock (_gate)
        {
            switch (_state)
            {
                case RunState.Disposed:
                case RunState.Stopped:
                    if (_stopFailure is not null)
                    {
                        ExceptionDispatchInfo.Capture(_stopFailure).Throw();
                    }

                    return;
                case RunState.Created:
                    throw new InvalidOperationException("A ProtoHost must be started before it can be stopped.");
                case RunState.Starting:
                    throw new InvalidOperationException(ProtoHostGuards.StartupStillInProgressStop);
                case RunState.Stopping:
                    throw new InvalidOperationException("ProtoHost shutdown is already in progress.");
                default:
                    _state = RunState.Stopping;
                    return;
            }
        }
    }

    /// <summary>Records the stop outcome and its failures, if any.</summary>
    public void CompleteStop(IReadOnlyList<Exception> failures)
    {
        lock (_gate)
        {
            _state = RunState.Stopped;
            _stopFailure = failures.Count switch
            {
                0 => null,
                1 => failures[0],
                _ => new AggregateException("One or more run hooks failed during shutdown.", failures)
            };
        }
    }

    /// <summary>
    /// Rejects a test that would read half a run: starting before the run's hooks and infrastructure
    /// finished, or after the run began shutting down, is a mistake rather than a wait.
    /// </summary>
    public void EnsureTestCanStart()
    {
        lock (_gate)
        {
            switch (_state)
            {
                case RunState.Starting:
                    throw new InvalidOperationException(
                        "A test cannot start while the ProtoHost is starting; start it after startup completes.");
                case RunState.Stopping:
                case RunState.Stopped:
                case RunState.Disposed:
                    throw new InvalidOperationException("A test cannot start after the ProtoHost begins shutting down.");
            }
        }
    }

    /// <summary>Whether the run reached Started and has not been stopped.</summary>
    public bool IsStarted
    {
        get
        {
            lock (_gate)
            {
                return _state == RunState.Started;
            }
        }
    }

    /// <summary>
    /// Checks that disposal may begin. The run stays stoppable until <see cref="CompleteDispose"/>, so
    /// disposal can still run the AfterRun hooks. Returns whether the host was already disposed.
    /// </summary>
    public bool EnsureCanDispose()
    {
        lock (_gate)
        {
            switch (_state)
            {
                case RunState.Starting:
                    throw new InvalidOperationException(ProtoHostGuards.StartupStillInProgressDispose);
                case RunState.Stopping:
                    // Accepting the transition here would let the in-flight stop write Stopped over
                    // Disposed once it finishes; the caller must dispose after the stop completes.
                    throw new InvalidOperationException(
                        "ProtoHost shutdown is still in progress; dispose it after the stop completes.");
                case RunState.Disposed:
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>Makes the final transition. A stop may have started since the check, so the guards apply again.</summary>
    public void CompleteDispose()
    {
        lock (_gate)
        {
            if (_state is RunState.Starting)
            {
                throw new InvalidOperationException(ProtoHostGuards.StartupStillInProgressDispose);
            }

            if (_state is RunState.Stopping)
            {
                throw new InvalidOperationException(
                    "ProtoHost shutdown is still in progress; dispose it after the stop completes.");
            }

            _state = RunState.Disposed;
        }
    }

    private enum RunState
    {
        Created,
        Starting,
        Started,
        Stopping,
        Stopped,
        Disposed
    }
}
