namespace ProtoTest.Hosting.Internal;

using ProtoTest.Core;

/// <summary>
/// Collects one worker's providers while its registration callback runs, then registers the worker as
/// a target chain: the first available provider serves the worker, and only its piece starts and
/// capabilities are declared. The piece is created lazily - a worker the environment runs never needs
/// a host factory for its entry point.
/// </summary>
internal sealed class ProtoWorkerChainBuilder<TProgram>(
    string name,
    ProtoWorkerOptions options,
    ProtoWorkerRegistry registry,
    string? applicationName = null) : IProtoWorkerBuilder
    where TProgram : class
{
    private readonly List<IProtoTargetProvider> _providers = [];
    private ProtoWorkerHost<TProgram>? _hostedWorker;

    public string WorkerName { get; } = name;

    /// <summary>Gets the worker capability every provider of this chain declares.</summary>
    private ProtoCapabilityDescriptor WorkerCapability { get; } = new(
        typeof(TProgram).Assembly.GetName().Name ?? typeof(TProgram).FullName!,
        ProtoCapabilityKinds.Worker,
        "ProtoTest.Hosting")
    {
        Instance = name
    };

    public IProtoWorkerBuilder Use(IProtoTargetProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (_providers.Any(existing => string.Equals(existing.Name, provider.Name, StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                $"A provider named '{provider.Name}' is already registered on worker '{WorkerName}'; " +
                "every provider of one worker needs its own name.",
                nameof(provider));
        }

        _providers.Add(provider);
        return this;
    }

    public IProtoWorkerBuilder UseEnvironment()
        => Use(new ProtoTargetProvider("environment", Condition: new EnvironmentServedCondition(applicationName))
        {
            Capabilities = [WorkerCapability]
        });

    public IProtoWorkerBuilder UseHost()
        => Use(new ProtoTargetProvider("host", HostedWorker())
        {
            Capabilities =
            [
                WorkerCapability,
                new ProtoCapabilityDescriptor("Test clock", ProtoCapabilityKinds.Clock, "ProtoTest.Hosting")
                {
                    Instance = WorkerName
                }
            ]
        });

    /// <summary>
    /// Registers the worker's target on the host builder. A worker with no provider falls back to
    /// <see cref="UseHost"/>, so the top-level registration keeps today's behavior.
    /// </summary>
    public void Register(IProtoHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (_providers.Count == 0)
        {
            UseHost();
        }

        var targetName = applicationName is null
            ? $"worker:{WorkerName}"
            : $"{applicationName}:worker:{WorkerName}";
        var providers = _providers.ToArray();
        builder.AddInfrastructure(targetName, chain =>
        {
            if (applicationName is not null)
            {
                // The worker's providers read the application's winner: whether the run hosts the
                // worker depends on how the application is served.
                chain.ResolveAfter(applicationName);
            }

            foreach (var provider in providers)
            {
                chain.Use(provider);
            }
        });
    }

    private ProtoWorkerHost<TProgram> HostedWorker()
        => _hostedWorker ??= ProtoWorkerHost<TProgram>.Create(WorkerName, options, registry);

    /// <summary>
    /// Available when the application the worker belongs to is served by a provider that does not run
    /// it in-process: the environment that hosts the application runs its worker.
    /// </summary>
    private sealed class EnvironmentServedCondition(string? applicationName) : IProtoProviderCondition
    {
        public bool IsSatisfied(ProtoProviderConditionContext context)
            => applicationName is not null
                && context.Target(applicationName) is { } application
                && !application.HasCapability(ProtoCapabilityKinds.Server);

        public string Describe(ProtoProviderConditionContext context)
        {
            if (applicationName is null)
            {
                return "The worker is not nested under an application, so no environment hosts it";
            }

            var application = context.Target(applicationName);
            return application is null
                ? $"The application '{applicationName}' must resolve its provider chain before the worker does"
                : $"The application '{applicationName}' runs in-process ('{application.ProviderName}'), so the run hosts its worker";
        }
    }
}
