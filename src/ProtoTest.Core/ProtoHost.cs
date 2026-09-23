namespace ProtoTest.Core;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core.Internal;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;

/// <summary>
/// Owns the ProtoTest service provider and exposes the public run and test lifecycle API.
/// The run state machine lives here; hook sequencing and the test lifecycle are delegated to focused
/// internal components.
/// </summary>
public sealed class ProtoHost : IAsyncDisposable
{
    private readonly IServiceProvider _rootServiceProvider;
    private readonly ProtoRunHooks _runHooks;
    private readonly ProtoTestLifecycle _testLifecycle;
    private readonly ProtoTraceSession _trace;
    private readonly ProtoLock _runGate = new();
    private readonly List<IProtoRunHook> _startedHooks = [];
    private RunState _state;
    private Exception? _stopFailure;

    public ProtoHost(IServiceProvider rootServiceProvider)
    {
        _rootServiceProvider = rootServiceProvider ?? throw new ArgumentNullException(nameof(rootServiceProvider));

        var testHooks = _rootServiceProvider.GetServices<IProtoTestHook>().ToArray();
        var runHooks = _rootServiceProvider.GetServices<IProtoRunHook>().ToArray();
        var testIdGenerator = _rootServiceProvider.GetService<IProtoTestIdGenerator>()
            ?? new NumericProtoTestIdGenerator();
        _trace = _rootServiceProvider.GetService<ProtoTraceSession>() ?? new ProtoTraceSession();

        _runHooks = new ProtoRunHooks(runHooks);
        _testLifecycle = new ProtoTestLifecycle(this, _rootServiceProvider, testHooks, testIdGenerator, _trace);
        ProtoHostRegistry.Register(this);
    }

    /// <summary>
    /// Gets the current test execution context for this asynchronous control flow.
    /// </summary>
    public static ProtoExecutionContext CurrentContext => ProtoTestLifecycle.CurrentContext;

    /// <summary>
    /// Gets the current test execution context, or <see langword="null"/> when none is active on this flow -
    /// the safe lookup for telemetry callbacks, which can run outside the test's context.
    /// </summary>
    public static ProtoExecutionContext? CurrentContextOrNull => ProtoTestLifecycle.TryGetCurrentContext;

    /// <summary>
    /// Gets the host owning the current test, or the sole active host outside a test.
    /// </summary>
    public static ProtoHost CurrentHost => ProtoHostRegistry.GetCurrent(ProtoTestLifecycle.CurrentHost);

    /// <summary>
    /// Finds the trace writer of the test an application span belongs to, matched by its W3C trace id.
    /// This is the safe lookup for application telemetry callbacks, which run outside the test's flow:
    /// <see cref="CurrentContextOrNull"/> is empty there and <see cref="CurrentHost"/> cannot be resolved.
    /// </summary>
    public static IProtoTraceWriter? FindTraceWriter(ActivityTraceId traceId)
        => ProtoHostRegistry.FindTraceWriter(traceId);

    /// <summary>
    /// Returns whether the host is composed with a capability of the given kind (optionally a specific
    /// name). Integrations declare capabilities when they are configured, so this answers what the host
    /// can actually do rather than what it was asked to do.
    /// </summary>
    public bool HasCapability(string kind, string? name = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        return _rootServiceProvider.GetServices<ProtoCapabilityDescriptor>().Any(capability =>
            string.Equals(capability.Kind, kind, StringComparison.Ordinal)
            && (name is null || string.Equals(capability.Name, name, StringComparison.Ordinal)));
    }

    public IConfiguration Configuration => _rootServiceProvider.GetRequiredService<IConfiguration>();

    // A disposed provider throws on lookup, and disposal is exactly when the settings must be cleared:
    // the host's own teardown paths treat "already gone" as "nothing left to clear".
    private ProtoInfrastructureSettings? InfrastructureSettings
    {
        get
        {
            try
            {
                return _rootServiceProvider.GetService<ProtoInfrastructureSettings>();
            }
            catch (ObjectDisposedException)
            {
                return null;
            }
        }
    }

    /// <summary>Gets immutable snapshots of the current run trace.</summary>
    public IProtoTraceSource Trace => _trace;

    /// <summary>
    /// Executes all suite-level BeforeRun hooks in ascending order, then records the capabilities the
    /// host is composed of as run entities. The whole start path runs once: a repeat call after a
    /// successful start is a no-op, and a call while a start is in flight is rejected rather than
    /// recording or starting anything twice.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_runGate)
        {
            switch (_state)
            {
                case RunState.Started:
                    return;
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
                    break;
            }
        }

        try
        {
            await _runHooks.RunBeforeAsync(_startedHooks, cancellationToken);

            // A retry after a failed start re-owns the run-scoped resources the rollback released.
            if (_rootServiceProvider.GetService<ProtoRunResourceStore>() is { } runResources)
            {
                runResources.ResetForRestart();
            }

            foreach (var capability in _rootServiceProvider.GetServices<ProtoCapabilityDescriptor>())
            {
                _trace.RunWriter.SetEntityState(
                    ProtoTraceEntityKinds.Capability,
                    $"{capability.Kind}:{capability.Name}",
                    capability.Name,
                    new Dictionary<string, string?>
                    {
                        ["capability.name"] = capability.Name,
                        ["capability.kind"] = capability.Kind,
                        ["capability.source"] = capability.Source
                    },
                    scope: "run",
                    change: "activated");
            }

            // Infrastructure starts before any test: the run owns it, records it, and lets an in-process
            // application receive the connection strings as host settings. Hosts built without the builder
            // (tests, embedded use) simply have none.
            var settings = _rootServiceProvider.GetService<ProtoInfrastructureSettings>() ?? new ProtoInfrastructureSettings();
            foreach (var registration in _rootServiceProvider.GetServices<ProtoInfrastructureRegistration>())
            {
                var infrastructure = registration.Infrastructure;
                await infrastructure.StartAsync(cancellationToken);
                var state = new Dictionary<string, string?>
                {
                    ["infrastructure.kind"] = infrastructure.Kind,
                    ["infrastructure.settings"] = string.Join(", ", registration.Settings)
                };
                if (infrastructure is IProtoConnectionInfrastructure connection)
                {
                    foreach (var key in registration.Settings)
                    {
                        settings.Set(key, connection.ConnectionString);
                    }
                }

                if (infrastructure is IProtoSettingsInfrastructure sourced)
                {
                    foreach (var (key, value) in sourced.Settings)
                    {
                        settings.Set(key, value);
                    }
                }

                _trace.RunWriter.SetEntityState(
                    infrastructure.Kind,
                    infrastructure.Id,
                    infrastructure.Description,
                    state,
                    scope: "run",
                    change: "started");
            }

            _trace.StartListening();
            lock (_runGate)
            {
                _state = RunState.Started;
            }
        }
        catch (Exception exception)
        {
            // Nothing that did start may leak: the completed run hooks run in reverse, which releases
            // the run's resources (the infrastructure started so far) in reverse registration order,
            // and the state returns to Created so a retry is possible.
            var failures = new List<Exception> { exception };
            await _runHooks.RunAfterAsync(_startedHooks, failures, cancellationToken);
            _startedHooks.Clear();
            lock (_runGate)
            {
                _state = RunState.Created;
            }

            // The released infrastructure's connection strings must not survive into a retry or outlive
            // the run: clear the keys they filled while they were alive.
            if (InfrastructureSettings is { } settings)
            {
                settings.Clear();
            }

            LifecycleExceptionHelper.ThrowIfAny(
                "ProtoHost startup failed and the run's started resources were released.", failures);
        }
    }

    /// <summary>
    /// Executes all suite-level AfterRun hooks in descending order. A stop rejected because a start is
    /// in flight changes nothing; a stop that ran and failed is remembered and rethrown on retry.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_runGate)
        {
            switch (_state)
            {
                case RunState.Disposed:
                    return;
                case RunState.Stopped:
                    // A stop that failed is remembered, not silently turned into a success: the run's
                    // hooks were attempted once and the caller must still see why they did not finish.
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
                    break;
            }
        }

        var exceptions = new List<Exception>();
        try
        {
            await _runHooks.RunAfterAsync(_startedHooks, exceptions, cancellationToken);
        }
        finally
        {
            _startedHooks.Clear();
            lock (_runGate)
            {
                _state = RunState.Stopped;
                _stopFailure = exceptions.Count switch
                {
                    0 => null,
                    1 => exceptions[0],
                    _ => new AggregateException("One or more run hooks failed during shutdown.", exceptions)
                };
            }

            // The run is over: the released infrastructure's connection strings must not stay readable,
            // and the trace is complete even when a hook failed to shut down.
            if (InfrastructureSettings is { } settings)
            {
                settings.Clear();
            }

            _trace.CompleteRun();
        }

        LifecycleExceptionHelper.ThrowIfAny("One or more run hooks failed during shutdown.", exceptions);
    }

    /// <summary>
    /// Starts a test lifecycle with an explicit numeric ID.
    /// </summary>
    public Task<ProtoExecutionContext> StartTestAsync(
        string testName,
        string testId,
        MethodInfo testMethod,
        IEnumerable<ProtoAttribute>? attributes = null,
        IProtoTestAttachmentPublisher? attachmentPublisher = null)
    {
        EnsureTestCanStart();
        return _testLifecycle.StartAsync(
            testName, ProtoTestId.Parse(testId), testMethod, attributes, attachmentPublisher);
    }

    /// <summary>
    /// Starts a test lifecycle with an ID generated by this host.
    /// </summary>
    public Task<ProtoExecutionContext> StartTestAsync(
        string testName,
        MethodInfo testMethod,
        IEnumerable<ProtoAttribute>? attributes = null,
        IProtoTestAttachmentPublisher? attachmentPublisher = null)
    {
        EnsureTestCanStart();
        return _testLifecycle.StartAsync(testName, testMethod, attributes, attachmentPublisher);
    }

    /// <summary>
    /// Rejects a test that would read half a run: starting before the run's hooks and infrastructure
    /// finished, or after the run began shutting down, is a mistake rather than a wait.
    /// </summary>
    private void EnsureTestCanStart()
    {
        lock (_runGate)
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

    /// <summary>
    /// Completes the active test using the exact lifecycle components that completed setup.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no test is active on the current async flow, so a missing or leaked context is
    /// reported instead of being silently ignored.
    /// </exception>
    public Task CompleteTestAsync()
    {
        if (ProtoTestLifecycle.TryGetCurrentContext is null)
        {
            throw new InvalidOperationException(
                "No ProtoTest test is active on this async flow. CompleteTestAsync requires a test " +
                "started by StartTestAsync on the same flow.");
        }

        return _testLifecycle.CompleteAsync(ProtoTestResult.Unknown);
    }

    /// <summary>
    /// Completes the active test and records the result reported by its framework adapter. Unlike the
    /// parameterless overload this stays a no-op when no test is active: adapters call it from
    /// teardown even when their framework skipped the test before the lifecycle started.
    /// </summary>
    public Task CompleteTestAsync(ProtoTestResult result)
        => _testLifecycle.CompleteAsync(result ?? throw new ArgumentNullException(nameof(result)));

    public async ValueTask DisposeAsync()
    {
        var exceptions = new List<Exception>();

        // The start path runs to Started: disposing mid-start would stop a run whose infrastructure
        // is still coming up and then let the start record itself as completed on a disposed host.
        lock (_runGate)
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
                    LifecycleExceptionHelper.ThrowIfAny("One or more run hooks failed during disposal.", exceptions);
                    return;
            }
        }

        // Completing the run here means `await using var host = ...` alone still runs AfterRun
        // hooks (report sinks, trace export). StopAsync is idempotent, so an explicit stop first
        // makes this a no-op.
        bool started;
        lock (_runGate)
        {
            started = _state == RunState.Started;
        }

        if (started)
        {
            try
            {
                await StopAsync(default);
            }
            catch (Exception exception)
            {
                exceptions.Add(exception);
            }
        }

        lock (_runGate)
        {
            // A stop may have started between the check above and here; the same guards apply.
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

        // Run-scoped resources outlive the run itself, so they are released once the reports are
        // written and before the provider they may depend on is disposed.
        try
        {
            if (_rootServiceProvider.GetService<ProtoRunResourceStore>() is { } runResources)
            {
                exceptions.AddRange(await runResources.ReleaseAllAsync(_trace.RunWriter));
            }
        }
        catch (Exception exception)
        {
            exceptions.Add(exception);
        }

        // Whichever path ran the teardown, the infrastructure is gone by now: its connection strings
        // must not remain readable through the settings object.
        if (InfrastructureSettings is { } infrastructureSettings)
        {
            infrastructureSettings.Clear();
        }

        try
        {
            await LifecycleExceptionHelper.DisposeAsyncOrSync(_rootServiceProvider);
        }
        catch (Exception exception)
        {
            exceptions.Add(exception);
        }
        finally
        {
            _trace.StopListening();
            _trace.CompleteRun();
            ProtoHostRegistry.Unregister(this);
        }

        LifecycleExceptionHelper.ThrowIfAny(
            "One or more resources failed to dispose.", exceptions);
    }

    /// <summary>
    /// The run's single state machine. Created can start; Starting rejects a second start, a stop and a
    /// disposal; Started is the only state a stop tears down; Stopped keeps a failed stop's failure for
    /// the retry that asks again; Disposed is final.
    /// </summary>
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
