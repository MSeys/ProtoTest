namespace ProtoTest.Core;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core.Internal;
using System.Diagnostics;
using System.Reflection;

/// <summary>
/// Owns the ProtoTest service provider and exposes the public run and test lifecycle API.
/// Lifecycle execution is delegated to focused internal components.
/// </summary>
public sealed class ProtoHost : IAsyncDisposable
{
    private const int StartCreated = 0;
    private const int StartInProgress = 1;
    private const int StartCompleted = 2;
    private const int StopInProgress = 3;

    private readonly IServiceProvider _rootServiceProvider;
    private readonly ProtoRunLifecycle _runLifecycle;
    private readonly ProtoTestLifecycle _testLifecycle;
    private readonly ProtoTraceSession _trace;
    private readonly ProtoLock _startGate = new();
    private int _startState;

    public ProtoHost(IServiceProvider rootServiceProvider)
    {
        _rootServiceProvider = rootServiceProvider ?? throw new ArgumentNullException(nameof(rootServiceProvider));

        var testHooks = _rootServiceProvider.GetServices<IProtoTestHook>().ToArray();
        var runHooks = _rootServiceProvider.GetServices<IProtoRunHook>().ToArray();
        var testIdGenerator = _rootServiceProvider.GetService<IProtoTestIdGenerator>()
            ?? new NumericProtoTestIdGenerator();
        _trace = _rootServiceProvider.GetService<ProtoTraceSession>() ?? new ProtoTraceSession();

        _runLifecycle = new ProtoRunLifecycle(runHooks);
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
        lock (_startGate)
        {
            if (_startState == StartCompleted)
            {
                return;
            }

            if (_startState == StartInProgress)
            {
                throw new InvalidOperationException("ProtoHost startup is already in progress.");
            }

            if (_startState == StopInProgress)
            {
                throw new InvalidOperationException(
                    "ProtoHost shutdown is in progress; start it after the stop completes.");
            }

            _startState = StartInProgress;
        }

        try
        {
            await _runLifecycle.StartAsync(cancellationToken);

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
            lock (_startGate)
            {
                _startState = StartCompleted;
            }
        }
        catch (Exception exception)
        {
            // Nothing that did start may leak: the run hooks run in reverse, which releases the run's
            // resources (the infrastructure started so far) in reverse registration order, and the
            // lifecycle returns to Created so a retry is possible.
            var failures = new List<Exception> { exception };
            await _runLifecycle.RollbackStartAsync(failures, cancellationToken);
            lock (_startGate)
            {
                _startState = StartCreated;
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
        int? previousStartState = null;
        lock (_startGate)
        {
            // The start path is not finished until it records StartCompleted, including the infrastructure
            // it starts after the run hooks: stopping here would tear down the run mid-start.
            if (_startState == StartInProgress)
            {
                throw new InvalidOperationException(
                    "ProtoHost startup is still in progress; stop it after startup completes.");
            }

            // A stop rejected by the lifecycle (or a second stop) must not touch the start state it did
            // not change.
            if (_startState is not StopInProgress)
            {
                previousStartState = _startState;
                _startState = StopInProgress;
            }
        }

        try
        {
            await _runLifecycle.StopAsync(cancellationToken);
        }
        finally
        {
            if (_runLifecycle.IsStopped)
            {
                lock (_startGate)
                {
                    _startState = StartCreated;
                }

                // The infrastructure those keys pointed at is gone; a retry or an in-process application
                // must not keep reading a released instance's connection string.
                if (InfrastructureSettings is { } settings)
                {
                    settings.Clear();
                }

                _trace.CompleteRun();
            }
            else if (previousStartState is { } restored)
            {
                lock (_startGate)
                {
                    _startState = restored;
                }
            }
        }
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
        _runLifecycle.EnsureTestCanStart();
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
        _runLifecycle.EnsureTestCanStart();
        return _testLifecycle.StartAsync(testName, testMethod, attributes, attachmentPublisher);
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

        // The start path runs to StartCompleted: disposing mid-start would stop a run whose infrastructure
        // is still coming up and then let the start record itself as completed on a disposed host.
        lock (_startGate)
        {
            if (_startState == StartInProgress)
            {
                throw new InvalidOperationException(
                    "ProtoHost startup is still in progress; dispose it after startup completes.");
            }
        }

        // Completing the run here means `await using var host = ...` alone still runs AfterRun
        // hooks (report sinks, trace export). StopAsync is idempotent, so an explicit stop first
        // makes this a no-op.
        if (_runLifecycle.IsStarted)
        {
            try
            {
                await _runLifecycle.StopAsync(default);
            }
            catch (Exception exception)
            {
                exceptions.Add(exception);
            }
        }

        if (!_runLifecycle.TryMarkDisposed())
        {
            LifecycleExceptionHelper.ThrowIfAny(
                "One or more run hooks failed during disposal.", exceptions);
            return;
        }

        // The host is finished: a later StartAsync must reach the lifecycle and be rejected there
        // rather than reading StartCompleted and reporting a start that cannot happen.
        lock (_startGate)
        {
            _startState = StartCreated;
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
            if (_rootServiceProvider is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (_rootServiceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
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
}
