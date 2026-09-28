namespace ProtoTest.Aspire;

using global::Aspire.Hosting;
using global::Aspire.Hosting.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ProtoTest.Aspire.Internal;
using ProtoTest.Core;

/// <summary>
/// One Aspire AppHost the run owns: the host starts it after the infrastructure registered before
/// it, each declared publish mapping fills the key it was registered under - an application's
/// <c>BaseUrl</c> for an endpoint, the target's declared key for a connection string - and the run
/// stops it after the reports are written. Only the resources the run selected are published: the
/// global key selects every resource, a resource's own key selects it, and a key configuration
/// already fills is never masked. The run's configuration and the settings earlier infrastructure
/// published travel to the AppHost as command-line arguments, so its own graph can read the same
/// addresses the suite resolved. Register it with <c>AddAspireAppHost</c> or reference it from a
/// target's providers with <c>UseAspireResource</c>.
/// </summary>
/// <typeparam name="TEntryPoint">A public type in the AppHost assembly; the testing host runs the
/// assembly's entry point in-process.</typeparam>
public sealed class ProtoAspireAppHost<TEntryPoint> : IProtoSettingsInfrastructure, IProtoConfiguredInfrastructure, IProtoStartupEvidence, IProtoAspireAppHost, IAsyncDisposable
    where TEntryPoint : class
{
    private readonly List<string> _resources;
    private readonly List<ProtoAspirePublish> _publishes = [];
    private readonly ProtoAspireOptions _options;
    private readonly ProtoLock _gate = new();
    private DistributedApplication? _application;
    private Dictionary<string, string> _settings = new(StringComparer.Ordinal);
    private Dictionary<string, string?> _startupEvidence = new(StringComparer.Ordinal);
    private int _started;
    private int _released;

    /// <summary>Creates the resource without starting it; the host starts it with the run.</summary>
    public ProtoAspireAppHost(IEnumerable<string> resources, Action<ProtoAspireOptions>? configure = null)
        : this(resources, configure, publishEndpoints: true)
    {
    }

    /// <summary>
    /// Creates the resource with or without the default "each resource's endpoint fills its
    /// application's BaseUrl" publishes. A piece referenced by provider chains declares its publishes
    /// per target instead, so every key it fills is one the target declared.
    /// </summary>
    internal ProtoAspireAppHost(
        IEnumerable<string> resources,
        Action<ProtoAspireOptions>? configure,
        bool publishEndpoints)
    {
        ArgumentNullException.ThrowIfNull(resources);
        _resources = [.. resources.Where(resource => !string.IsNullOrWhiteSpace(resource))];
        if (_resources.Count == 0)
        {
            throw new ArgumentException(
                "Declare at least one Aspire resource; each becomes the application target its endpoint is published under.",
                nameof(resources));
        }

        if (_resources.Distinct(StringComparer.Ordinal).Count() != _resources.Count)
        {
            throw new ArgumentException("An Aspire resource is declared twice; declare each resource once.", nameof(resources));
        }

        _options = new ProtoAspireOptions();
        configure?.Invoke(_options);
        _options.Validate(_resources);
        if (publishEndpoints)
        {
            foreach (var resource in _resources)
            {
                // A resource the suite mapped as a connection string has no HTTP endpoint to publish.
                if (!_options.HasConnectionString(resource))
                {
                    _publishes.Add(new ProtoAspirePublish(resource, BaseUrlKey(resource), ProtoAspirePublishKind.Endpoint));
                }
            }
        }

        foreach (var (resource, key) in _options.ConnectionStrings)
        {
            _publishes.Add(new ProtoAspirePublish(resource, key, ProtoAspirePublishKind.ConnectionString));
        }
    }

    /// <summary>Gets the Aspire resources this AppHost publishes.</summary>
    public IReadOnlyList<string> Resources => _resources;

    /// <summary>Gets the application name the resource is published under.</summary>
    public string ApplicationFor(string resource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        if (!_resources.Contains(resource, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"'{resource}' is not one of this AppHost's resources ({string.Join(", ", _resources.Select(name => $"'{name}'"))}).",
                nameof(resource));
        }

        return _options.ApplicationFor(resource);
    }

    /// <summary>Gets the key the started AppHost fills with the resource's address.</summary>
    public string BaseUrlKey(string resource)
        => $"{ProtoApplication.SectionPath}:{ApplicationFor(resource)}:BaseUrl";

    /// <summary>Gets the key the started AppHost fills for the resource: its connection string when mapped, its address otherwise.</summary>
    internal string KeyFor(string resource)
        => _options.ConnectionStrings.TryGetValue(resource, out var key) ? key : BaseUrlKey(resource);

    /// <summary>Gets every key the started AppHost fills with the resources' addresses, for <c>AddInfrastructure</c>.</summary>
    public IReadOnlyList<string> BaseUrlKeys => _resources.Select(BaseUrlKey).ToArray();

    /// <summary>Gets every key any publish mapping fills, endpoints and connection strings alike.</summary>
    internal IReadOnlyList<string> PublishKeys
        => [.. _publishes.Select(publish => publish.Key).Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// Records that the started AppHost publishes <paramref name="resource"/>'s connection string
    /// under <paramref name="key"/>, so the run's readers resolve the AppHost's database or broker the
    /// same way they resolve an application's address. The target's declared registration fills the key
    /// when the AppHost starts and the environment has no value for it.
    /// </summary>
    /// <param name="resource">The AppHost resource, for example <c>postgres</c>.</param>
    /// <param name="key">The target's declared key, for example <c>ConnectionStrings:Northstar</c>.</param>
    public ProtoAspireAppHost<TEntryPoint> MapConnectionString(string resource, string key)
    {
        AddPublish(resource, key, ProtoAspirePublishKind.ConnectionString, replaceEndpoints: true);
        return this;
    }

    /// <summary>
    /// Records a publish mapping this AppHost fills; the resource must be one it declares. When
    /// <paramref name="replaceEndpoints"/> is set, an endpoint mapping for the resource is removed: a
    /// resource serves one target as either an address or a connection string.
    /// </summary>
    internal void AddPublish(
        string resource,
        string key,
        ProtoAspirePublishKind kind,
        bool replaceEndpoints = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!_resources.Contains(resource, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"'{resource}' is not one of this AppHost's resources ({string.Join(", ", _resources.Select(name => $"'{name}'"))}); " +
                "declare it with AddAspireAppHost or reference it from a target's providers.",
                nameof(resource));
        }

        if (kind == ProtoAspirePublishKind.ConnectionString && replaceEndpoints)
        {
            _publishes.RemoveAll(publish =>
                string.Equals(publish.Resource, resource, StringComparison.Ordinal)
                && publish.Kind == ProtoAspirePublishKind.Endpoint);
        }

        if (!_publishes.Any(publish =>
                string.Equals(publish.Resource, resource, StringComparison.Ordinal)
                && string.Equals(publish.Key, key, StringComparison.Ordinal)
                && publish.Kind == kind))
        {
            _publishes.Add(new ProtoAspirePublish(resource, key, kind));
        }
    }

    /// <summary>Gets every publish mapping the AppHost fills, in declaration order.</summary>
    internal IReadOnlyList<ProtoAspirePublish> Publishes => _publishes;

    /// <summary>Gets the endpoint of the resource the started AppHost reads, defaulting to <c>http</c>.</summary>
    public string EndpointName(string resource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        if (!_resources.Contains(resource, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"'{resource}' is not one of this AppHost's resources ({string.Join(", ", _resources.Select(name => $"'{name}'"))}).",
                nameof(resource));
        }

        return _options.EndpointFor(resource);
    }

    /// <inheritdoc />
    public string Id => $"aspire:{typeof(TEntryPoint).FullName}";

    IReadOnlyList<string> IProtoAspireAppHost.PublishKeys => PublishKeys;

    /// <inheritdoc />
    public string Kind => ProtoCapabilityKinds.Aspire;

    /// <inheritdoc />
    public string Description => $"Aspire AppHost · {typeof(TEntryPoint).Assembly.GetName().Name} ({string.Join(", ", _resources)})";

    /// <inheritdoc />
    public ProtoResourceScope Scope => ProtoResourceScope.Run;

    /// <summary>Gets the addresses the started AppHost published, keyed as the applications read them.</summary>
    public IReadOnlyDictionary<string, string> Settings
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<string, string>(_settings, StringComparer.Ordinal);
            }
        }
    }

    /// <summary>Gets what starting the AppHost observed: the entry point and every published endpoint.</summary>
    public IReadOnlyDictionary<string, string?> StartupEvidence
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<string, string?>(_startupEvidence, StringComparer.Ordinal);
            }
        }
    }

    internal bool IsStarted => Volatile.Read(ref _started) != 0;

    internal bool SameCompositionAs(ProtoAspireAppHost<TEntryPoint> other)
        => _resources.SequenceEqual(other._resources, StringComparer.Ordinal)
            && _options.SameAs(other._options)
            && _publishes.SequenceEqual(other._publishes);

    internal string Describe()
        => string.Join(", ", _resources.Select(resource =>
            $"'{resource}' as '{_options.ApplicationFor(resource)}' ({_options.EndpointFor(resource)})"));

    /// <summary>
    /// Whether the failure means the Aspire orchestration runtime is unavailable: the DCP executable
    /// or the dashboard binaries the AppHost starts with could not be found. Suites catch the typed
    /// exception this maps to and skip instead of failing.
    /// </summary>
    internal static bool IsRuntimeMissing(Exception exception)
    {
        var queue = new Queue<Exception>();
        queue.Enqueue(exception);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current is OptionsValidationException validation
                && (validation.Message.Contains("CliPath", StringComparison.Ordinal)
                    || validation.Message.Contains("DashboardPath", StringComparison.Ordinal)
                    || validation.Failures.Any(failure =>
                        failure.Contains("CliPath", StringComparison.Ordinal)
                        || failure.Contains("DashboardPath", StringComparison.Ordinal))))
            {
                return true;
            }

            if (current.InnerException is not null)
            {
                queue.Enqueue(current.InnerException);
            }

            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    queue.Enqueue(inner);
                }
            }
        }

        return false;
    }

    /// <inheritdoc />
    public ValueTask StartAsync(ProtoInfrastructureContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return StartCoreAsync(context, cancellationToken);
    }

    private async ValueTask StartCoreAsync(ProtoInfrastructureContext context, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_released != 0)
            {
                _released = 0;
            }

            if (_started != 0)
            {
                return;
            }
        }

        var entryPoint = typeof(TEntryPoint).FullName!;
        DistributedApplication application;
        try
        {
            var testing = await DistributedApplicationTestingBuilder
                .CreateAsync<TEntryPoint>(ComposeArgs(context), cancellationToken)
                .ConfigureAwait(false);
            application = await testing.BuildAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!IsRuntimeMissing(exception))
        {
            throw new InvalidOperationException(
                $"The Aspire AppHost '{entryPoint}' could not be built: {exception.Message}", exception);
        }
        catch (Exception exception)
        {
            throw new ProtoAspireUnavailableException(
                $"The Aspire AppHost '{entryPoint}' could not start because the Aspire orchestration runtime is unavailable: {exception.Message}",
                exception);
        }

        try
        {
            await application.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await SafeDisposeAsync(application).ConfigureAwait(false);
            throw MapStartFailure(entryPoint, exception);
        }

        Dictionary<string, string> settings;
        try
        {
            settings = await ResolveSettingsAsync(application, context.Configuration, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await SafeDisposeAsync(application).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"The Aspire AppHost '{entryPoint}' started but its endpoints could not be resolved: {exception.Message}",
                exception);
        }

        lock (_gate)
        {
            if (_released == 0)
            {
                _application = application;
                _settings = settings;
                _startupEvidence = BuildEvidence(settings, context.Configuration);
                Volatile.Write(ref _started, 1);
                return;
            }
        }

        await SafeDisposeAsync(application).ConfigureAwait(false);
        throw new ObjectDisposedException(GetType().FullName);
    }

    /// <summary>
    /// Resolves every publish mapping the run selected and the environment does not already fill: a
    /// configured key wins over the AppHost's value (published settings take precedence at use time),
    /// and an unselected resource is another provider's target, so publishing it would mask that
    /// provider. The AppHost still starts for the mappings that need a value.
    /// </summary>
    private async Task<Dictionary<string, string>> ResolveSettingsAsync(
        DistributedApplication application,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var publish in _publishes)
        {
            if (!ProtoAspireOptions.IsSelected(configuration, publish.Resource)
                || !string.IsNullOrWhiteSpace(configuration[publish.Key]))
            {
                continue;
            }

            settings[publish.Key] = publish.Kind switch
            {
                ProtoAspirePublishKind.Endpoint => ResolveEndpoint(application, publish),
                _ => await ResolveConnectionStringAsync(application, publish, cancellationToken).ConfigureAwait(false)
            };
        }

        return settings;
    }

    private string ResolveEndpoint(DistributedApplication application, ProtoAspirePublish publish)
    {
        var endpointName = _options.EndpointFor(publish.Resource);
        try
        {
            return application.GetEndpoint(publish.Resource, endpointName).ToString();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Aspire resource '{publish.Resource}' has no '{endpointName}' endpoint. Add one to the AppHost or point UseEndpoint at the one it exposes.",
                exception);
        }
    }

    private static async Task<string> ResolveConnectionStringAsync(
        DistributedApplication application,
        ProtoAspirePublish publish,
        CancellationToken cancellationToken)
    {
        string? connectionString;
        try
        {
            connectionString = await application.GetConnectionStringAsync(publish.Resource, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Aspire resource '{publish.Resource}' has no connection string. Add one to the AppHost or map an endpoint resource instead.",
                exception);
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Aspire resource '{publish.Resource}' published no connection string; the resource must expose one to fill '{publish.Key}'.");
        }

        return connectionString;
    }

    private Dictionary<string, string?> BuildEvidence(Dictionary<string, string> settings, IConfiguration configuration)
    {
        var evidence = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["aspire.entry_point"] = typeof(TEntryPoint).FullName,
        };
        foreach (var publish in _publishes)
        {
            if (publish.Kind == ProtoAspirePublishKind.Endpoint)
            {
                evidence[$"aspire.resource.{publish.Resource}.endpoint"] = _options.EndpointFor(publish.Resource);
            }

            if (settings.TryGetValue(publish.Key, out var value))
            {
                evidence[$"aspire.resource.{publish.Resource}.{publish.KindTag}"] = value;
            }
            else if (!ProtoAspireOptions.IsSelected(configuration, publish.Resource))
            {
                // The resource belongs to another provider in this run, so the AppHost did not fill it.
                evidence[$"aspire.resource.{publish.Resource}.{publish.KindTag}_source"] = "not selected";
            }
            else
            {
                // A configured key wins over the AppHost's value, so this run published none.
                evidence[$"aspire.resource.{publish.Resource}.{publish.KindTag}_source"] = "configuration";
            }
        }

        return evidence;
    }

    private string[] ComposeArgs(ProtoInfrastructureContext context)
        => [.. Merge(context.Configuration, context.Settings.Values, _options.Values)
            .Where(pair => pair.Value is not null)
            .Select(pair => $"--{pair.Key}={pair.Value}")];

    /// <summary>
    /// Builds the overlay the run hands the AppHost's entry point: the suite's configuration first,
    /// the settings earlier infrastructure published second, and this AppHost's own option values
    /// last, so each layer wins over the one before it - the worker host's order. The AppHost reads
    /// the overlay as command-line configuration, and a mapping whose key configuration already fills
    /// still loses to that configured value at use time. A key with a null value carries no argument,
    /// so the AppHost's own sources answer for it.
    /// </summary>
    internal static Dictionary<string, string?> Merge(
        IConfiguration configuration,
        IReadOnlyDictionary<string, string> settings,
        IReadOnlyDictionary<string, string?> options)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(options);

        var merged = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in configuration.AsEnumerable())
        {
            merged[key] = value;
        }

        foreach (var (key, value) in settings)
        {
            merged[key] = value;
        }

        foreach (var (key, value) in options)
        {
            merged[key] = value;
        }

        return merged;
    }

    private static Exception MapStartFailure(string entryPoint, Exception exception)
    {
        if (IsRuntimeMissing(exception))
        {
            return new ProtoAspireUnavailableException(
                $"The Aspire AppHost '{entryPoint}' could not start because the Aspire orchestration runtime is unavailable " +
                $"(the DCP executable or dashboard binaries are missing): {exception.Message}",
                exception);
        }

        return new InvalidOperationException(
            $"The Aspire AppHost '{entryPoint}' failed to start: {exception.Message}", exception);
    }

    private static async ValueTask SafeDisposeAsync(DistributedApplication application)
    {
        try
        {
            await application.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // The start failure is what the caller needs to see.
        }
    }

    /// <inheritdoc />
    public async ValueTask ReleaseAsync(ProtoResourceReleaseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        DistributedApplication? application;
        lock (_gate)
        {
            if (_released != 0)
            {
                return;
            }

            _released = 1;
            application = _application;
            _application = null;
            _settings = new Dictionary<string, string>(StringComparer.Ordinal);
            Volatile.Write(ref _started, 0);
        }

        if (application is null)
        {
            return;
        }

        try
        {
            await application.StopAsync(context.CancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await application.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        DistributedApplication? application;
        lock (_gate)
        {
            if (_released != 0)
            {
                return ValueTask.CompletedTask;
            }

            _released = 1;
            application = _application;
            _application = null;
            _settings = new Dictionary<string, string>(StringComparer.Ordinal);
            Volatile.Write(ref _started, 0);
        }

        return ReleaseCoreAsync(application);
    }

    private static async ValueTask ReleaseCoreAsync(DistributedApplication? application)
    {
        if (application is null)
        {
            return;
        }

        try
        {
            await application.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            await application.DisposeAsync().ConfigureAwait(false);
        }
    }
}
