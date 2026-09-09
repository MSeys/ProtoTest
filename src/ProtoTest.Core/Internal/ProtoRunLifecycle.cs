namespace ProtoTest.Core;

internal sealed class ProtoRunLifecycle(IEnumerable<IProtoRunHook> hooks)
{
    private readonly object _gate = new();
    private readonly IReadOnlyList<IProtoRunHook> _hooks = hooks.OrderBy(hook => hook.Order).ToArray();
    private IReadOnlyList<IProtoRunHook> _startedHooks = [];
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
                case State.Stopped:
                case State.Disposed:
                    return;
                case State.Created:
                    throw new InvalidOperationException("A ProtoHost must be started before it can be stopped.");
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
        }

        LifecycleExceptionHelper.ThrowIfAny("One or more run hooks failed during shutdown.", exceptions);
    }

    public void EnsureTestCanStart()
    {
        lock (_gate)
        {
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
            if (_state == State.Disposed)
            {
                return false;
            }

            _state = State.Disposed;
            return true;
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
