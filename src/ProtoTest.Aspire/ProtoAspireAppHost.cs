namespace ProtoTest.Aspire;

using global::Aspire.Hosting;
using global::Aspire.Hosting.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ProtoTest.Core;

/// <summary>
/// One Aspire AppHost the run owns: the host starts it after the infrastructure registered before
/// it, each declared resource's endpoint is published as its application's <c>BaseUrl</c> so the
/// application's clients resolve that one address, and the run stops it after the reports are
/// written. Register it with <c>AddAspireAppHost</c>, which declares the keys it fills: a run that
/// configures those keys points at that environment instead of starting the AppHost.
/// </summary>
/// <typeparam name="TEntryPoint">A public type in the AppHost assembly; the testing host runs the
/// assembly's entry point in-process.</typeparam>
public sealed class ProtoAspireAppHost<TEntryPoint> : IProtoSettingsInfrastructure, IProtoConfiguredInfrastructure, IProtoStartupEvidence, IAsyncDisposable
    where TEntryPoint : class
{
    private readonly List<string> _resources;
    private readonly ProtoAspireOptions _options;
    private readonly ProtoLock _gate = new();
    private DistributedApplication? _application;
    private Dictionary<string, string> _settings = new(StringComparer.Ordinal);
    private Dictionary<string, string?> _startupEvidence = new(StringComparer.Ordinal);
    private int _started;
    private int _released;

    /// <summary>Creates the resource without starting it; the host starts it with the run.</summary>
    public ProtoAspireAppHost(IEnumerable<string> resources, Action<ProtoAspireOptions>? configure = null)
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

    /// <summary>Gets every key the started AppHost fills, for <c>AddInfrastructure</c>.</summary>
    public IReadOnlyList<string> BaseUrlKeys => _resources.Select(BaseUrlKey).ToArray();

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
        => _resources.SequenceEqual(other._resources, StringComparer.Ordinal) && _options.SameAs(other._options);

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
                .CreateAsync<TEntryPoint>(ComposeArgs(), cancellationToken)
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
            settings = ResolveEndpoints(application, context.Configuration);
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
                _startupEvidence = BuildEvidence(settings);
                Volatile.Write(ref _started, 1);
                return;
            }
        }

        await SafeDisposeAsync(application).ConfigureAwait(false);
        throw new ObjectDisposedException(GetType().FullName);
    }

    /// <summary>
    /// Resolves the endpoint of every declared resource whose key the environment does not already
    /// fill: a configured key wins over the AppHost's address (published settings take precedence at
    /// use time), so publishing it would mask the environment's value. The AppHost still starts for
    /// the resources that do need an address.
    /// </summary>
    private Dictionary<string, string> ResolveEndpoints(DistributedApplication application, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var resource in _resources)
        {
            var key = BaseUrlKey(resource);
            if (!string.IsNullOrWhiteSpace(configuration[key]))
            {
                continue;
            }

            var endpointName = _options.EndpointFor(resource);
            Uri endpoint;
            try
            {
                endpoint = application.GetEndpoint(resource, endpointName);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Aspire resource '{resource}' has no '{endpointName}' endpoint. Add one to the AppHost or point UseEndpoint at the one it exposes.",
                    exception);
            }

            settings[key] = endpoint.ToString();
        }

        return settings;
    }

    private Dictionary<string, string?> BuildEvidence(Dictionary<string, string> settings)
    {
        var evidence = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["aspire.entry_point"] = typeof(TEntryPoint).FullName,
        };
        foreach (var resource in _resources)
        {
            evidence[$"aspire.resource.{resource}.endpoint"] = _options.EndpointFor(resource);
            if (settings.TryGetValue(BaseUrlKey(resource), out var address))
            {
                evidence[$"aspire.resource.{resource}.address"] = address;
            }
            else
            {
                // A configured key wins over the AppHost's address, so this run published none.
                evidence[$"aspire.resource.{resource}.address_source"] = "configuration";
            }
        }

        return evidence;
    }

    private string[] ComposeArgs()
        => _options.Values
            .Where(pair => pair.Value is not null)
            .Select(pair => $"--{pair.Key}={pair.Value}")
            .ToArray();

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
