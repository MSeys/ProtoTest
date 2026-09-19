namespace ProtoTest.Core.Internal;

using System.Runtime.ExceptionServices;

internal sealed class ProtoRunLifecycle(IEnumerable<IProtoRunHook> hooks)
{
    private readonly ProtoLock _gate = new();
    private readonly IReadOnlyList<IProtoRunHook> _hooks = [.. hooks.OrderBy(hook => hook.Order)];
    private IReadOnlyList<IProtoRunHook> _startedHooks = [];
    private Exception? _stopFailure;
    private State _state;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            switch (_state)
            {
                case State.Started:
                    return;
                case State.Starting:
                    throw new InvalidOperationException("ProtoHost startup is already in progress.");
                case State.Stopped:
                case State.Disposed:
                    throw new InvalidOperationException("A ProtoHost cannot be started after it has stopped.");
                default:
                    _state = State.Starting;
                    break;
            }
        }

        var completedHooks = new List<IProtoRunHook>();
        try
        {
            foreach (var hook in _hooks)
            {
                await hook.BeforeRunAsync(cancellationToken);
                completedHooks.Add(hook);
            }

            _startedHooks = completedHooks;
            lock (_gate)
            {
                _state = State.Started;
            }
        }
        catch (Exception exception)
        {
            var exceptions = new List<Exception> { exception };
            foreach (var hook in completedHooks.AsEnumerable().Reverse())
            {
                await LifecycleExceptionHelper.CaptureAsync(
                    () => hook.AfterRunAsync(cancellationToken), exceptions);
            }

            lock (_gate)
            {
                _state = State.Created;
            }

            LifecycleExceptionHelper.ThrowIfAny(
                "ProtoHost startup failed and completed hooks were rolled back.", exceptions);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            switch (_state)
            {
                case State.Disposed:
                    return;
                case State.Stopped:
                    // A stop that failed is remembered, not silently turned into a success: the run's
                    // hooks were attempted once and the caller must still see why they did not finish.
                    if (_stopFailure is not null)
                    {
                        ExceptionDispatchInfo.Capture(_stopFailure).Throw();
                    }

                    return;
                case State.Created:
                    throw new InvalidOperationException("A ProtoHost must be started before it can be stopped.");
                case State.Starting:
                    throw new InvalidOperationException(
                        "ProtoHost startup is still in progress; stop it after startup completes.");
                case State.Stopping:
                    throw new InvalidOperationException("ProtoHost shutdown is already in progress.");
                default:
                    _state = State.Stopping;
                    break;
            }
        }

        var exceptions = new List<Exception>();
        foreach (var hook in _startedHooks.Reverse())
        {
            await LifecycleExceptionHelper.CaptureAsync(
                () => hook.AfterRunAsync(cancellationToken), exceptions);
        }

        _startedHooks = [];
        lock (_gate)
        {
            _state = State.Stopped;
            _stopFailure = exceptions.Count switch
            {
                0 => null,
                1 => exceptions[0],
                _ => new AggregateException("One or more run hooks failed during shutdown.", exceptions)
            };
        }

        LifecycleExceptionHelper.ThrowIfAny("One or more run hooks failed during shutdown.", exceptions);
    }

    /// <summary>
    /// Tears down a run whose start failed after the run hooks had started: the completed hooks run in
    /// reverse and the lifecycle returns to <see cref="State.Created"/> so <see cref="StartAsync"/> can
    /// be retried. Failures join <paramref name="failures"/> so the original start failure stays visible.
    /// </summary>
    public async Task RollbackStartAsync(List<Exception> failures, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_state != State.Started)
            {
                return;
            }

            _state = State.Stopping;
        }

        foreach (var hook in _startedHooks.Reverse())
        {
            await LifecycleExceptionHelper.CaptureAsync(
                () => hook.AfterRunAsync(cancellationToken), failures);
        }

        _startedHooks = [];
        lock (_gate)
        {
            _state = State.Created;
        }
    }

    /// <summary>Gets whether the run is currently started and awaiting shutdown.</summary>
    public bool IsStarted
    {
        get
        {
            lock (_gate)
            {
                return _state == State.Started;
            }
        }
    }

    /// <summary>Gets whether the run has finished the stop sequence, successfully or not.</summary>
    public bool IsStopped
    {
        get
        {
            lock (_gate)
            {
                return _state is State.Stopped or State.Disposed;
            }
        }
    }

    public void EnsureTestCanStart()
    {
        lock (_gate)
        {
            if (_state == State.Starting)
            {
                throw new InvalidOperationException(
                    "A test cannot start while the ProtoHost is starting; start it after startup completes.");
            }

            if (_state is State.Stopping or State.Stopped or State.Disposed)
            {
                throw new InvalidOperationException("A test cannot start after the ProtoHost begins shutting down.");
            }
        }
    }

    public bool TryMarkDisposed()
    {
        lock (_gate)
        {
            switch (_state)
            {
                case State.Disposed:
                    return false;
                case State.Starting:
                    throw new InvalidOperationException(
                        "ProtoHost startup is still in progress; dispose it after startup completes.");
                case State.Stopping:
                    // Accepting the transition here would let the in-flight stop write Stopped over
                    // Disposed once it finishes; the caller must dispose after the stop completes.
                    throw new InvalidOperationException(
                        "ProtoHost shutdown is still in progress; dispose it after the stop completes.");
                default:
                    _state = State.Disposed;
                    return true;
            }
        }
    }

    private enum State
    {
        Created,
        Starting,
        Started,
        Stopping,
        Stopped,
        Disposed
    }
}
