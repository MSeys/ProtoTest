namespace ProtoTest.Hosting.Internal;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ProtoTest.Core;

/// <summary>
/// One background worker the run owns: the suite builds the worker's own host through its entry point,
/// starts it after the infrastructure it may depend on, and stops it when the run ends. The lease the
/// run takes is the host itself; configuration seen by the worker is the run's settings plus whatever
/// <see cref="ProtoWorkerOptions"/> set.
/// </summary>
internal sealed class ProtoWorkerHost<TProgram> : IProtoConfiguredInfrastructure
    where TProgram : class
{
    private readonly string _name;
    private readonly ProtoWorkerOptions _options;
    private readonly ProtoWorkerRegistry _registry;
    private readonly object _gate = new();
    private Func<string[], object>? _factory;
    private IReadOnlyDictionary<string, string?> _configuration = new Dictionary<string, string?>(StringComparer.Ordinal);
    private TimeProvider _timeProvider = TimeProvider.System;
    private IHost? _host;

    public ProtoWorkerHost(string name, ProtoWorkerOptions options, ProtoWorkerRegistry registry)
    {
        _name = name;
        _options = options;
        _registry = registry;
    }

    /// <inheritdoc />
    public string Id => _name;

    /// <inheritdoc />
    public string Kind => ProtoCapabilityKinds.Worker;

    /// <inheritdoc />
    public string Description => $"Worker · {typeof(TProgram).Name} ({_name})";

    /// <inheritdoc />
    public ProtoResourceScope Scope => ProtoResourceScope.Run;

    /// <summary>Completes the constructor with the resolver-built host factory.</summary>
    public void UseFactory(Func<string[], object> factory) => _factory = factory;

    /// <inheritdoc />
    public ValueTask StartAsync(ProtoInfrastructureContext context, CancellationToken cancellationToken = default)
        => StartCoreAsync(context, cancellationToken);

    private async ValueTask StartCoreAsync(ProtoInfrastructureContext context, CancellationToken cancellationToken)
    {
        var factory = _factory ?? throw new InvalidOperationException(
            $"The worker host '{_name}' has no host factory; call builder.AddWorkerHost<{typeof(TProgram).Name}>() instead of registering it directly.");

        IReadOnlyDictionary<string, string?> configuration;
        lock (_gate)
        {
            _configuration = Merge(context, _options.Values);
            configuration = _configuration;
            _timeProvider = context.TimeProvider;
        }

        await ReleaseStartedAsync(CancellationToken.None).ConfigureAwait(false);

        // The worker's entry point needs its own content root and application name: it runs inside the
        // test process, but reads the worker's appsettings and reports the worker as the application.
        // The run's merged overlay travels as command-line arguments so an entry point that builds its
        // host from args reads final-precedence values inside Main, before Build() fires HostBuilding.
        var arguments = ProtoWorkerArguments.Compose(
            Path.GetDirectoryName(typeof(TProgram).Assembly.Location) ?? AppContext.BaseDirectory,
            typeof(TProgram).Assembly.FullName ?? typeof(TProgram).Assembly.GetName().Name ?? typeof(TProgram).FullName!,
            configuration);

        object built;
        try
        {
            built = factory(arguments);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"The worker host '{_name}' ({typeof(TProgram).FullName}) could not be built: {exception.Message}", exception);
        }

        if (built is not IHost host)
        {
            throw new InvalidOperationException(
                $"The entry point of {typeof(TProgram).FullName} built {built?.GetType().FullName ?? "nothing"}, not an IHost.");
        }

        try
        {
            await host.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await StopAndDisposeAsync(host, CancellationToken.None).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"The worker host '{_name}' ({typeof(TProgram).FullName}) failed to start: {exception.Message}", exception);
        }

        lock (_gate)
        {
            _host = host;
        }

        _registry.Add(_name, typeof(TProgram), host);
    }

    /// <inheritdoc />
    public async ValueTask ReleaseAsync(ProtoResourceReleaseContext context)
    {
        // A release also runs after a failed start, when nothing was started or the failure already
        // disposed the host; that is not a failure of its own.
        await ReleaseStartedAsync(context.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies the run's configuration and the suite's options to the worker builder, whichever hosting
    /// pattern the worker's entry point used, and replaces its <see cref="TimeProvider"/> with the run's
    /// clock bridge so worker code that reads time sees the suite's clock. The entry point already
    /// received the same overlay as arguments; this is the fallback for an entry point that ignores them.
    /// </summary>
    internal void ConfigureBuilder(object builder)
    {
        IReadOnlyDictionary<string, string?> configuration;
        TimeProvider timeProvider;
        lock (_gate)
        {
            configuration = _configuration;
            timeProvider = _timeProvider;
        }

        // A null value has no setting to carry: the overlay's contract is "keys with no value are not
        // passed" (a ProtoWorkerOptions value is never null - Set turns null into an empty setting).
        var values = configuration
            .Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        switch (builder)
        {
            case IHostBuilder hostBuilder:
                hostBuilder.ConfigureAppConfiguration(configurationBuilder => configurationBuilder.AddInMemoryCollection(values));
                hostBuilder.ConfigureServices(services => ReplaceTimeProvider(services, timeProvider));
                break;
            // HostApplicationBuilder and WebApplicationBuilder are both IHostApplicationBuilder.
            case IHostApplicationBuilder applicationBuilder:
                applicationBuilder.Configuration.AddInMemoryCollection(values);
                ReplaceTimeProvider(applicationBuilder.Services, timeProvider);
                break;
            default:
                throw new InvalidOperationException(
                    $"The worker host '{_name}' ({typeof(TProgram).FullName}) built a " +
                    $"{builder?.GetType().FullName ?? "null"} builder, which ProtoTest.Hosting does not recognise. " +
                    "Build the worker's host with Host.CreateApplicationBuilder, Host.CreateDefaultBuilder or WebApplication.CreateBuilder.");
        }
    }

    private static void ReplaceTimeProvider(IServiceCollection services, TimeProvider timeProvider)
    {
        services.RemoveAll<TimeProvider>();
        services.AddSingleton(timeProvider);
    }

    private async ValueTask ReleaseStartedAsync(CancellationToken cancellationToken)
    {
        IHost? host;
        lock (_gate)
        {
            host = _host;
            _host = null;
        }

        if (host is null)
        {
            return;
        }

        _registry.Remove(_name);
        await StopAndDisposeAsync(host, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask StopAndDisposeAsync(IHost host, CancellationToken cancellationToken)
    {
        try
        {
            await host.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (host is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                (host as IDisposable)?.Dispose();
            }
        }
    }

    private static IReadOnlyDictionary<string, string?> Merge(
        ProtoInfrastructureContext context,
        IReadOnlyDictionary<string, string?> options)
    {
        var merged = new Dictionary<string, string?>(StringComparer.Ordinal);

        // The suite's configuration first: the worker's own appsettings already loaded, this is the
        // suite's deliberate overlay. Started infrastructure's settings win over it for the keys they
        // publish, and the suite's AddWorkerHost options win over everything.
        foreach (var (key, value) in context.Configuration.AsEnumerable())
        {
            merged[key] = value;
        }

        foreach (var (key, value) in context.Settings.Values)
        {
            merged[key] = value;
        }

        foreach (var (key, value) in options)
        {
            merged[key] = value;
        }

        return merged;
    }
}
