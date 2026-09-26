namespace ProtoTest.Core.Internal;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Sequences a run's start and stop: the BeforeRun hooks, the capability records, the skipped
/// declarations, the infrastructure loop and the trace listener - and the reverse unwind of the hooks a
/// failed start completed. The host owns the run state machine; this owns the work each transition
/// performs, the way <see cref="ProtoTestLifecycle"/> owns a test's.
/// </summary>
internal sealed class ProtoRunLifecycle
{
    private readonly IServiceProvider _services;
    private readonly ProtoRunHooks _runHooks;
    private readonly ProtoTraceSession _trace;
    private readonly ProtoClock _clock;
    private readonly List<IProtoRunHook> _startedHooks = [];

    public ProtoRunLifecycle(
        IServiceProvider services,
        IEnumerable<IProtoRunHook> hooks,
        ProtoTraceSession trace,
        ProtoClock clock)
    {
        _services = services;
        _runHooks = new ProtoRunHooks(hooks);
        _trace = trace;
        _clock = clock;
    }

    /// <summary>
    /// Runs the whole start path: hooks, recorded capabilities, skipped declarations, infrastructure in
    /// registration order, then the trace listener. A failure stops the path; the caller rolls back.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _runHooks.RunBeforeAsync(_startedHooks, cancellationToken);

        // A retry after a failed start re-owns the run-scoped resources the rollback released.
        var runResources = _services.GetService<ProtoRunResourceStore>();
        runResources?.ResetForRestart();

        foreach (var capability in _services.GetServices<ProtoCapabilityDescriptor>())
        {
            _trace.RunWriter.SetEntityState(
                ProtoTraceEntityKinds.Capability,
                CapabilityId(capability),
                capability.Name,
                new Dictionary<string, string?>
                {
                    ["capability.name"] = capability.Name,
                    ["capability.kind"] = capability.Kind,
                    ["capability.source"] = capability.Source,
                    ["capability.instance"] = capability.Instance
                },
                scope: "run",
                change: "activated");
        }

        if (_services.GetService<ProtoSkippedCapabilities>() is { Capabilities.Count: > 0 } skippedCapabilities)
        {
            // The capability is absent from the run overview: the environment either already
            // provides what the dropped integration would serve, or cannot provide the address it
            // needs. The event keeps the decision visible in the trace, names the deciding keys
            // and says which condition decided it.
            foreach (var skipped in skippedCapabilities.Capabilities)
            {
                var capability = skipped.Capability;
                _trace.RunWriter.WriteEvent(
                    "capability.skipped",
                    $"Skipped · {capability.Name}",
                    capability.Source,
                    phase: ProtoTracePhase.Run,
                    outcome: ProtoTraceOutcome.Skipped,
                    attributes: new Dictionary<string, string?>
                    {
                        ["capability.name"] = capability.Name,
                        ["capability.kind"] = capability.Kind,
                        ["capability.instance"] = capability.Instance,
                        ["capability.keys"] = string.Join(", ", skipped.Keys),
                        ["capability.reason"] = skipped.Reason
                    });
            }
        }

        // Infrastructure starts before any test: the run owns it, records it, and lets an in-process
        // application receive the connection strings as host settings. Hosts built without the builder
        // (tests, embedded use) simply have none.
        var settings = _services.GetService<ProtoInfrastructureSettings>() ?? new ProtoInfrastructureSettings();
        var configuration = _services.GetService<IConfiguration>() ?? new ConfigurationBuilder().Build();
        var timeProvider = _services.GetService<TimeProvider>() ?? new ProtoTestTimeProvider(_clock);
        var readinessOptions = _services.GetService<ProtoReadinessOptions>();
        var skippedInfrastructure = _services.GetService<ProtoSkippedInfrastructure>()?.Ids;
        var registrations = _services.GetServices<ProtoInfrastructureRegistration>().ToArray();
        var inProcessServers = _services.GetServices<ProtoCapabilityDescriptor>()
            .Where(capability => string.Equals(capability.Kind, ProtoCapabilityKinds.Server, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(capability.Instance))
            .Select(capability => capability.Instance!)
            .ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < registrations.Length; index++)
        {
            var registration = registrations[index];
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

            // What a piece starting here cannot see yet: the settings keys infrastructure
            // registered after it declares, and the applications an in-process server backs. The
            // application readiness probe uses them to name an ordering mistake honestly.
            var pendingSettings = new HashSet<string>(StringComparer.Ordinal);
            for (var later = index + 1; later < registrations.Length; later++)
            {
                if (skippedInfrastructure is not null
                    && skippedInfrastructure.Contains(registrations[later].Infrastructure.Id))
                {
                    continue;
                }

                pendingSettings.UnionWith(registrations[later].Settings);
            }

            var infrastructureContext = new ProtoInfrastructureContext(settings, configuration, timeProvider)
            {
                Readiness = readinessOptions,
                PendingSettings = pendingSettings,
                InProcessServerApplications = inProcessServers
            };

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
    }

    /// <summary>
    /// Unwinds the completed hooks that own state, in reverse, and clears the settings a failed start's
    /// infrastructure published. The start failure stays first; the unwind failures are appended so the
    /// host reports both.
    /// </summary>
    public async Task<List<Exception>> RollbackAsync(Exception startFailure, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startFailure);
        // Nothing that owns state may leak: every completed hook unwinds in reverse, so a user
        // hook's BeforeRun setup is undone. Hooks whose AfterRun writes run evidence (gates,
        // reports, the archive) are not unwound: a run that never finished starting produces no
        // evidence, and the state returns to Created so a retry is possible.
        var failures = new List<Exception> { startFailure };
        IReadOnlyList<IProtoRunHook> rollbackHooks =
            [.. _startedHooks.Where(hook => hook is not IProtoRunEvidenceHook)];
        await _runHooks.RunAfterAsync(rollbackHooks, failures, cancellationToken);
        _startedHooks.Clear();

        // The released infrastructure's connection strings must not survive into a retry or outlive
        // the run: clear the keys they filled while they were alive.
        ClearInfrastructureSettings();
        return failures;
    }

    /// <summary>Runs the completed hooks' AfterRun in reverse, collecting failures instead of stopping.</summary>
    public async Task StopAsync(List<Exception> exceptions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exceptions);
        try
        {
            await _runHooks.RunAfterAsync(_startedHooks, exceptions, cancellationToken);
        }
        finally
        {
            _startedHooks.Clear();
        }
    }

    /// <summary>
    /// Forgets the values started infrastructure published, so a retry or a later reader cannot see a
    /// released instance's connection string. A disposed provider has nothing left to clear.
    /// </summary>
    public void ClearInfrastructureSettings()
    {
        try
        {
            _services.GetService<ProtoInfrastructureSettings>()?.Clear();
        }
        catch (ObjectDisposedException)
        {
            // A disposed provider throws on lookup, and disposal is exactly when the settings clear.
        }
    }

    // A capability that describes one instance carries it in the entity id, so two live instances of
    // the same named capability stay two run entities instead of overwriting each other.
    private static string CapabilityId(ProtoCapabilityDescriptor capability)
        => capability.Instance is { Length: > 0 } instance
            ? $"{capability.Kind}:{capability.Name}:{instance}"
            : $"{capability.Kind}:{capability.Name}";
}
