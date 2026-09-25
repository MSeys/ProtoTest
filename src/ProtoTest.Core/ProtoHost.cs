namespace ProtoTest.Core;

using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core.Internal;

/// <summary>
/// Owns the ProtoTest service provider and exposes the public run and test lifecycle API.
/// The run state machine lives in <see cref="ProtoRunStateMachine"/>; hook sequencing and the test
/// lifecycle are delegated to focused internal components.
/// </summary>
public sealed class ProtoHost : IAsyncDisposable
{
    private readonly IServiceProvider _rootServiceProvider;
    private readonly ProtoRunHooks _runHooks;
    private readonly ProtoTestLifecycle _testLifecycle;
    private readonly ProtoTraceSession _trace;
    private readonly ProtoClock _clock;
    private readonly ProtoRunStateMachine _runState = new();
    private readonly List<IProtoRunHook> _startedHooks = [];

    // The builder is the only composition path: it registers the stores, gates and hooks the host's
    // reporting depends on, so a provider assembled by hand cannot produce a host that silently
    // loses findings and resource reports.
    internal ProtoHost(IServiceProvider rootServiceProvider)
    {
        _rootServiceProvider = rootServiceProvider ?? throw new ArgumentNullException(nameof(rootServiceProvider));

        var testHooks = _rootServiceProvider.GetServices<IProtoTestHook>().ToArray();
        var runHooks = _rootServiceProvider.GetServices<IProtoRunHook>().ToArray();
        var testIdGenerator = _rootServiceProvider.GetService<IProtoTestIdGenerator>()
            ?? new NumericProtoTestIdGenerator();
        _trace = _rootServiceProvider.GetService<ProtoTraceSession>() ?? new ProtoTraceSession();
        // A host built through the builder always has a clock; a provider assembled by hand gets one here.
        _clock = _rootServiceProvider.GetService<ProtoClock>() ?? new ProtoClock();

        _runHooks = new ProtoRunHooks(runHooks);
        _testLifecycle = new ProtoTestLifecycle(this, _rootServiceProvider, testHooks, testIdGenerator, _trace, _clock);
        _clock.Advanced += OnRunClockAdvanced;
        ProtoHostRegistry.Register(this);
    }

    /// <summary>
    /// Gets the run's clock. Tests get their own clock seeded from it, so advancing time inside a test
    /// stays inside that test; advancing this one moves the whole run, including worker hosts.
    /// </summary>
    public ProtoClock Clock => _clock;

    /// <summary>
    /// Finds the clock of the test with the given id, or <see langword="null"/> when no such test is
    /// running. This is the lookup an in-process hosting integration uses to link a request the test
    /// caused back to the test's clock.
    /// </summary>
    public static ProtoClock? FindClock(string testId) => ProtoClockLocator.Find(testId);

    private void OnRunClockAdvanced(ProtoClockChange change)
    {
        _trace.RunWriter.SetEntityState(
            ProtoTraceEntityKinds.Clock,
            "clock:run",
            "Run clock",
            new Dictionary<string, string?>
            {
                ["clock.utcNow"] = change.CurrentUtc.ToString("O"),
                ["clock.timezone"] = _clock.LocalTimeZone.Id
            },
            scope: "run",
            change: "advanced");
        _trace.RunWriter.WriteEvent(
            "clock.advance",
            $"Run clock advanced by {change.Delta:g}",
            "ProtoTest.Core",
            attributes: new Dictionary<string, string?>
            {
                ["clock.delta"] = change.Delta.ToString("g"),
                ["clock.previousUtc"] = change.PreviousUtc.ToString("O"),
                ["clock.utcNow"] = change.CurrentUtc.ToString("O")
            },
            entityKind: ProtoTraceEntityKinds.Clock,
            entityId: "clock:run");
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
    /// Finds the trace writer for a span: the supplied activity, or <see cref="Activity.Current"/> when
    /// none is given. The one-line form for a telemetry callback that has an activity in hand.
    /// </summary>
    public static IProtoTraceWriter? FindTraceWriter(Activity? activity = null)
    {
        var span = activity ?? Activity.Current;
        return span is null ? null : FindTraceWriter(span.TraceId);
    }

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
        if (!_runState.BeginStart())
        {
            return;
        }

        try
        {
            await _runHooks.RunBeforeAsync(_startedHooks, cancellationToken);

            // A retry after a failed start re-owns the run-scoped resources the rollback released.
            var runResources = _rootServiceProvider.GetService<ProtoRunResourceStore>();
            runResources?.ResetForRestart();

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
            var configuration = _rootServiceProvider.GetService<IConfiguration>() ?? new ConfigurationBuilder().Build();
            var infrastructureContext = new ProtoInfrastructureContext(
                settings,
                configuration,
                _rootServiceProvider.GetService<TimeProvider>() ?? new ProtoTestTimeProvider(_clock));
            var skippedInfrastructure = _rootServiceProvider.GetService<ProtoSkippedInfrastructure>()?.Ids;
            foreach (var registration in _rootServiceProvider.GetServices<ProtoInfrastructureRegistration>())
            {
                var infrastructure = registration.Infrastructure;

                if (skippedInfrastructure is not null && skippedInfrastructure.Contains(infrastructure.Id))
                {
                    // Every address this piece would fill is already configured; starting it would
                    // shadow the environment's values, so the run records it as skipped, not owned.
                    _trace.RunWriter.SetEntityState(
                        infrastructure.Kind,
                        infrastructure.Id,
                        infrastructure.Description,
                        new Dictionary<string, string?>
                        {
                            ["infrastructure.kind"] = infrastructure.Kind,
                            ["infrastructure.settings"] = string.Join(", ", registration.Settings),
                            ["infrastructure.state"] = "skipped",
                            ["infrastructure.reason"] = "already configured"
                        },
                        scope: "run",
                        change: "skipped");
                    continue;
                }

                // Starting it again makes this a new ownership period: its release must run again.
                runResources?.Rearm(infrastructure);
                if (infrastructure is IProtoConfiguredInfrastructure configured)
                {
                    // Pieces that need the run's collected state (a worker reading a broker a container
                    // just started, a readiness probe reading a published address) receive it here.
                    await configured.StartAsync(infrastructureContext, cancellationToken);
                }
                else
                {
                    await infrastructure.StartAsync(cancellationToken);
                }
                var state = new Dictionary<string, string?>
                {
                    ["infrastructure.kind"] = infrastructure.Kind,
                    ["infrastructure.settings"] = string.Join(", ", registration.Settings)
                };
                if (infrastructure is IProtoStartupEvidence evidence)
                {
                    foreach (var (key, value) in evidence.StartupEvidence)
                    {
                        state[key] = value;
                    }
                }
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
            _runState.CompleteStart();
        }
        catch (Exception exception)
        {
            // Nothing that did start may leak: the run-resource hook releases the infrastructure started
            // so far, in reverse registration order. Gates, reports and the archive do not run for a run
            // that never finished starting, and the state returns to Created so a retry is possible.
            var failures = new List<Exception> { exception };
            IReadOnlyList<IProtoRunHook> rollbackHooks =
                [.. _startedHooks.OfType<ProtoRunResourceHook>().Cast<IProtoRunHook>()];
            await _runHooks.RunAfterAsync(rollbackHooks, failures, cancellationToken);
            _startedHooks.Clear();
            _runState.RollbackStart();

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
        _runState.BeginStop();

        var exceptions = new List<Exception>();
        try
        {
            await _runHooks.RunAfterAsync(_startedHooks, exceptions, cancellationToken);
        }
        finally
        {
            _startedHooks.Clear();
            _runState.CompleteStop(exceptions);

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
    private void EnsureTestCanStart() => _runState.EnsureTestCanStart();

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
        if (_runState.EnsureCanDispose())
        {
            LifecycleExceptionHelper.ThrowIfAny("One or more run hooks failed during disposal.", exceptions);
            return;
        }

        // Completing the run here means `await using var host = ...` alone still runs AfterRun
        // hooks (report sinks, trace export). StopAsync is idempotent, so an explicit stop first
        // makes this a no-op.
        if (_runState.IsStarted)
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

        // A stop may have started between the check above and here; the same guards apply.
        _runState.CompleteDispose();

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
}
