namespace ProtoTest.Core;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

public sealed class ProtoHost : IAsyncDisposable
{
    private static readonly AsyncLocal<ContextState?> _currentContext = new();
    private static ProtoHost? _currentHost;
    private readonly object _lifecycleGate = new();
    private readonly IServiceProvider _rootServiceProvider;
    private readonly IEnumerable<IProtoTestHook> _hooks;
    private readonly IEnumerable<IProtoRunHook> _runHooks;
    private HostState _state;

    public ProtoHost(IServiceProvider rootServiceProvider)
    {
        _rootServiceProvider = rootServiceProvider ?? throw new ArgumentNullException(nameof(rootServiceProvider));

        if (Interlocked.CompareExchange(ref _currentHost, this, null) is not null)
        {
            throw new InvalidOperationException(
                "Only one ProtoHost can be active in a process. Dispose the existing host before creating another one.");
        }

        _hooks = _rootServiceProvider.GetServices<IProtoTestHook>();
        _runHooks = _rootServiceProvider.GetServices<IProtoRunHook>();
    }

    /// <summary>
    /// Gets the current test execution context for this asynchronous control flow.
    /// </summary>
    public static ProtoExecutionContext CurrentContext => _currentContext.Value?.Context
        ?? throw new InvalidOperationException("No active ProtoExecutionContext available on this thread.");

    /// <summary>
    /// Gets the active host instance.
    /// </summary>
    public static ProtoHost CurrentHost => _currentHost
        ?? throw new InvalidOperationException("No active ProtoHost is available.");

    public IConfiguration Configuration => _rootServiceProvider.GetRequiredService<IConfiguration>();

    /// <summary>
    /// Executes all suite-level BeforeRun hooks in ascending order.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_lifecycleGate)
        {
            switch (_state)
            {
                case HostState.Started:
                    return;
                case HostState.Starting:
                    throw new InvalidOperationException("ProtoHost startup is already in progress.");
                case HostState.Stopped:
                case HostState.Disposed:
                    throw new InvalidOperationException("A ProtoHost cannot be started after it has stopped.");
                default:
                    _state = HostState.Starting;
                    break;
            }
        }

        try
        {
            foreach (var hook in _runHooks.OrderBy(h => h.Order))
            {
                await hook.BeforeRunAsync(cancellationToken);
            }

            lock (_lifecycleGate)
            {
                _state = HostState.Started;
            }
        }
        catch
        {
            lock (_lifecycleGate)
            {
                _state = HostState.Created;
            }

            throw;
        }
    }

    /// <summary>
    /// Executes all suite-level AfterRun hooks in descending order.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_lifecycleGate)
        {
            switch (_state)
            {
                case HostState.Stopped:
                case HostState.Disposed:
                    return;
                case HostState.Created:
                    throw new InvalidOperationException("A ProtoHost must be started before it can be stopped.");
                case HostState.Stopping:
                    throw new InvalidOperationException("ProtoHost shutdown is already in progress.");
                default:
                    _state = HostState.Stopping;
                    break;
            }
        }

        try
        {
            foreach (var hook in _runHooks.OrderByDescending(h => h.Order))
            {
                await hook.AfterRunAsync(cancellationToken);
            }

            lock (_lifecycleGate)
            {
                _state = HostState.Stopped;
            }
        }
        catch
        {
            lock (_lifecycleGate)
            {
                _state = HostState.Started;
            }

            throw;
        }
    }

    /// <summary>
    /// Starts a test lifecycle, creates its scoped execution context, and executes all before hooks.
    /// </summary>
    public Task<ProtoExecutionContext> StartTestAsync(
        string testName,
        string testId,
        MethodInfo testMethod,
        IEnumerable<ProtoAttribute>? attributes = null)
    {
        if (_currentContext.Value?.Context is not null)
        {
            throw new InvalidOperationException(
                "A ProtoExecutionContext is already active on this async flow. Complete the active test before starting another one.");
        }

        var scope = _rootServiceProvider.CreateScope();
        var context = new ProtoExecutionContext(testName, scope, testId, testMethod);
        _currentContext.Value = new ContextState(context);

        return ExecuteBeforeHooksAsync(context, attributes);
    }

    private async Task<ProtoExecutionContext> ExecuteBeforeHooksAsync(
        ProtoExecutionContext context,
        IEnumerable<ProtoAttribute>? attributes)
    {
        try
        {
            foreach (var hook in _hooks.OrderBy(x => x.Order))
            {
                await hook.BeforeTestAsync(context);
            }

            if (attributes != null)
            {
                foreach (var attribute in attributes.OrderBy(x => x.Order))
                {
                    await attribute.BeforeTestAsync(context);
                }
            }

            return context;
        }
        catch
        {
            try
            {
                await context.DisposeAsync();
            }
            finally
            {
                ClearCurrentContext();
            }

            throw;
        }
    }

    /// <summary>
    /// Completes the active test lifecycle, executes after hooks, disposes its scope, and clears ambient state.
    /// </summary>
    public async Task CompleteTestAsync(IEnumerable<ProtoAttribute>? attributes = null)
    {
        var context = _currentContext.Value?.Context;
        if (context == null) return;

        try
        {
            if (attributes != null)
            {
                foreach (var attribute in attributes.OrderByDescending(x => x.Order))
                {
                    await attribute.AfterTestAsync(context);
                }
            }

            foreach (var hook in _hooks.OrderByDescending(x => x.Order))
            {
                await hook.AfterTestAsync(context);
            }
        }
        finally
        {
            try
            {
                await context.DisposeAsync();
            }
            finally
            {
                ClearCurrentContext();
            }
        }
    }

    private void ClearCurrentContext()
    {
        if (_currentContext.Value is not null)
        {
            _currentContext.Value.Context = null;
            _currentContext.Value = null;
        }
    }

    private sealed class ContextState(ProtoExecutionContext context)
    {
        public ProtoExecutionContext? Context { get; set; } = context;
    }

    public async ValueTask DisposeAsync()
    {
        lock (_lifecycleGate)
        {
            if (_state == HostState.Disposed)
            {
                return;
            }

            _state = HostState.Disposed;
        }

        try
        {
            if (_rootServiceProvider is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (_rootServiceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        finally
        {
            Interlocked.CompareExchange(ref _currentHost, null, this);
        }
    }

    private enum HostState
    {
        Created,
        Starting,
        Started,
        Stopping,
        Stopped,
        Disposed
    }
}