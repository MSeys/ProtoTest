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
    private readonly ProtoRunLifecycle _runLifecycle;
    private readonly ProtoTestLifecycle _testLifecycle;
    private readonly ProtoTraceSession _trace;
    private readonly ProtoClock _clock;
    private readonly ProtoClockRegistry _clockRegistry;
    private readonly ProtoRunStateMachine _runState = new();

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
        // The registry is a host service so hosting integrations resolve the owning host's; a provider
        // assembled by hand gets a host-local one, cleared when the host is disposed.
        _clockRegistry = _rootServiceProvider.GetService<ProtoClockRegistry>() ?? new ProtoClockRegistry();

        _runLifecycle = new ProtoRunLifecycle(_rootServiceProvider, runHooks, _trace, _clock);
        _testLifecycle = new ProtoTestLifecycle(
            this, _rootServiceProvider, testHooks, testIdGenerator, _trace, _clock, _clockRegistry);
        _clock.Advanced += OnRunClockAdvanced;
        ProtoHostRegistry.Register(this);
    }

    /// <summary>
    /// Gets the run's clock. Tests get their own clock seeded from it, so advancing time inside a test
    /// stays inside that test; advancing this one moves the whole run, including worker hosts.
    /// </summary>
    public ProtoClock Clock => _clock;

    /// <summary>
    /// Finds this host's clock of the test with the given id, or <see langword="null"/> when no such
    /// test is running. The lookup is scoped to the owning host: two hosts that share a test id each
    /// resolve their own clock, and a finished test's clock only leaves its own host's registry. This
    /// is the lookup an in-process hosting integration uses to link a request the test caused back to
    /// the test's clock.
    /// </summary>
    public ProtoClock? FindClock(string testId) => _clockRegistry.Find(testId);

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
    public static ProtoHost CurrentHost => ProtoHostRegistry.GetCurrent();

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
        => HasCapability(kind, name, instance: null);

    /// <summary>
    /// Returns whether the host is composed with a capability of the given kind narrowed by the
    /// descriptor <paramref name="name"/> and/or the <paramref name="instance"/> it describes (an
    /// <c>AddAspNetCoreServer</c> name, an application an in-process device transport belongs to). A
    /// <see langword="null"/> filter matches anything; every non-null filter must match. The two-argument
    /// form matches the descriptor name only - use this overload to address an instance such as a named
    /// server.
    /// </summary>
    public bool HasCapability(string kind, string? name, string? instance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        return _rootServiceProvider.GetServices<ProtoCapabilityDescriptor>().Any(capability =>
            string.Equals(capability.Kind, kind, StringComparison.Ordinal)
            && (name is null || string.Equals(capability.Name, name, StringComparison.Ordinal))
            && (instance is null || string.Equals(capability.Instance, instance, StringComparison.Ordinal)));
    }

    /// <summary>
    /// Returns whether the host declares an application of this name through <c>AddApplication</c>, so a
    /// skip condition can answer whether the suite composed the application under test rather than
    /// whether an address happens to be configured.
    /// </summary>
    public bool HasApplication(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _rootServiceProvider.GetServices<ProtoApplicationClients>()
            .Any(application => string.Equals(application.ApplicationName, name, StringComparison.Ordinal));
    }

    /// <summary>
    /// Finds the suite-level reason a capability gate reports when it skips, declared through
    /// <c>AddCapabilityReason</c>. A reason declared for the exact <paramref name="name"/> wins over one
    /// declared for <paramref name="kind"/> as a whole; <see langword="null"/> when the suite declared no
    /// reason (the gate then uses its own default).
    /// </summary>
    public string? FindCapabilityReason(string kind, string? name = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        var reasons = _rootServiceProvider.GetServices<ProtoCapabilityReason>().ToArray();
        return reasons
                .FirstOrDefault(reason => string.Equals(reason.Kind, kind, StringComparison.Ordinal)
                    && string.Equals(reason.Name, name, StringComparison.Ordinal))
                ?.Reason
            ?? (name is null
                ? null
                : reasons
                    .FirstOrDefault(reason => string.Equals(reason.Kind, kind, StringComparison.Ordinal)
                        && reason.Name is null)
                    ?.Reason);
    }

    public IConfiguration Configuration => _rootServiceProvider.GetRequiredService<IConfiguration>();

    /// <summary>Gets immutable snapshots of the current run trace.</summary>
    public IProtoTraceSource Trace => _trace;

    /// <summary>
    /// Runs the run lifecycle's start path once: hooks, capability records, skipped declarations and
    /// the run's infrastructure, then the trace listener. A repeat call after a successful start is a
    /// no-op, and a call while a start is in flight is rejected rather than starting anything twice.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (!_runState.BeginStart())
        {
            return;
        }

        try
        {
            await _runLifecycle.StartAsync(cancellationToken);
            _runState.CompleteStart();
        }
        catch (Exception exception)
        {
            // Nothing that owns state may leak: the run lifecycle unwinds every completed hook in
            // reverse, while hooks whose AfterRun writes run evidence (gates, reports, the archive) stay
            // silent for a run that never finished starting. The state returns to Created so a retry is
            // possible.
            var failures = await _runLifecycle.RollbackAsync(exception, cancellationToken);
            _runState.RollbackStart();
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
            await _runLifecycle.StopAsync(exceptions, cancellationToken);
        }
        finally
        {
            _runState.CompleteStop(exceptions);

            // The run is over: the released infrastructure's connection strings must not stay readable,
            // and the trace is complete even when a hook failed to shut down.
            _runLifecycle.ClearInfrastructureSettings();
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
    /// Starts a test lifecycle with a caller-supplied attribute set, an attachment publisher and a
    /// cancellation token for the test. Setup I/O that can observe cancellation - the SQL connection
    /// open and transaction begin - does; the token reaches hooks and attributes through
    /// <see cref="ProtoExecutionContext.CancellationToken"/>.
    /// </summary>
    public Task<ProtoExecutionContext> StartTestAsync(
        string testName,
        MethodInfo testMethod,
        IEnumerable<ProtoAttribute>? attributes,
        IProtoTestAttachmentPublisher? attachmentPublisher,
        CancellationToken cancellationToken)
    {
        EnsureTestCanStart();
        return _testLifecycle.StartAsync(testName, testMethod, attributes, attachmentPublisher, cancellationToken);
    }

    /// <summary>
    /// Starts a test lifecycle with an explicit numeric ID and a cancellation token for the test.
    /// Setup I/O that can observe cancellation - the SQL connection open and transaction begin - does;
    /// the token reaches hooks and attributes through <see cref="ProtoExecutionContext.CancellationToken"/>.
    /// </summary>
    public Task<ProtoExecutionContext> StartTestAsync(
        string testName,
        string testId,
        MethodInfo testMethod,
        CancellationToken cancellationToken)
    {
        EnsureTestCanStart();
        return _testLifecycle.StartAsync(
            testName,
            ProtoTestId.Parse(testId),
            testMethod,
            attributes: null,
            attachmentPublisher: null,
            cancellationToken);
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
    /// Starts a test lifecycle with an ID generated by this host and a cancellation token for the test.
    /// </summary>
    public Task<ProtoExecutionContext> StartTestAsync(
        string testName,
        MethodInfo testMethod,
        CancellationToken cancellationToken)
    {
        EnsureTestCanStart();
        return _testLifecycle.StartAsync(
            testName,
            testMethod,
            attributes: null,
            attachmentPublisher: null,
            cancellationToken);
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
        _runLifecycle.ClearInfrastructureSettings();

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
            // No test clock may survive the host: a disposed host has no running tests to link to.
            _clockRegistry.Clear();
            ProtoHostRegistry.Unregister(this);
        }

        LifecycleExceptionHelper.ThrowIfAny(
            "One or more resources failed to dispose.", exceptions);
    }
}
