namespace ProtoTest.AspNetCore.Internal;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoTest.Core;
using ProtoTest.Web.Pages;

/// <summary>
/// Initializes an in-process ASP.NET Core test server and a per-test client for it.
/// </summary>
/// <typeparam name="TProgram">The entry point class of the ASP.NET Core application under test.</typeparam>
internal sealed class AspNetCoreClientInitializer<TProgram> : IProtoClientInitializer<HttpClient>, IAsyncDisposable
    where TProgram : class
{
    private readonly Action<IWebHostBuilder>? _configureWebHost;
    private readonly Action<WebApplicationFactoryClientOptions>? _configureClientOptions;
    private readonly IAspNetCoreServerLifetime<TProgram> _serverLifetime;
    private int _pageInventoryRecorded;

    public AspNetCoreClientInitializer(
        string name,
        Action<IWebHostBuilder>? configureWebHost,
        Action<WebApplicationFactoryClientOptions>? configureClientOptions,
        AspNetCoreServerLifetime lifetime)
    {
        Name = name;
        _configureWebHost = configureWebHost;
        _configureClientOptions = configureClientOptions;
        _serverLifetime = lifetime switch
        {
            AspNetCoreServerLifetime.PerRun => new PerRunServerLifetime<TProgram>(),
            _ => new PerTestServerLifetime<TProgram>()
        };
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public Task<bool> TryInitializeAsync(ProtoExecutionContext context)
    {
        // The transport can serve several protocol chains that share its name; the initializer hook
        // invokes it once, and this guard keeps a direct second call from registering a second client.
        if (context.TryClient<HttpClient>(Name) is not null)
        {
            return Task.FromResult(true);
        }

        // A configured address means the application runs elsewhere - published, container-backed, or
        // started by infrastructure. The in-process server steps aside and the address serves the
        // application's HTTP clients instead; this is the same registration a suite makes for every
        // environment, with no mode conditional in Setup. HTTP clients read static configuration only:
        // an address a started piece published belongs to the sessions that drive that process.
        var address = ProtoApplication.BaseUrl(context.Configuration, Name);
        if (!string.IsNullOrWhiteSpace(address))
        {
            InitializePublished(context, address);
            return Task.FromResult(true);
        }

        var lease = _serverLifetime.Acquire(context, Name, CombinedConfigure);
        var server = lease.Server;
        var clientOptions = new WebApplicationFactoryClientOptions();
        _configureClientOptions?.Invoke(clientOptions);
        var handlers = CreateClientHandlers(clientOptions)
            .Append(new ProtoTraceContextHandler())
            .ToArray();
        var client = server.Factory.CreateDefaultClient(clientOptions.BaseAddress, handlers);
        context.RegisterClient(client, Name);
        var serverState = new AspNetCoreServerState(
            Name,
            typeof(TProgram).FullName!,
            typeof(TProgram).Name,
            _serverLifetime.Kind,
            lease.Reused,
            _configureWebHost is not null,
            _configureClientOptions is not null);
        context.Trace.SetEntityState(
            ProtoTraceEntityKinds.Server,
            serverState.EntityId,
            serverState.DisplayName,
            serverState.ToAttributes(),
            scope: context.TestName,
            change: "initialized");
        context.Trace.WriteEvent(
            "aspnetcore.server.initialize",
            $"ASP.NET Core server · {Name}",
            "ProtoTest.AspNetCore",
            ProtoTracePhase.Setup,
            ProtoTraceOutcome.Succeeded,
            serverState.ToAttributes(),
            entityKind: ProtoTraceEntityKinds.Server,
            entityId: serverState.EntityId);
        RecordPageInventory(context, server);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Serves the application's HTTP clients from its configured address without starting a test server.
    /// The address is also where the web session and the address-relative device clients look, so every
    /// consumer of the application agrees on where it runs.
    /// </summary>
    private void InitializePublished(ProtoExecutionContext context, string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var baseAddress)
            || (baseAddress.Scheme != Uri.UriSchemeHttp && baseAddress.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"Application '{Name}' has the configured base address '{address}', which is not an absolute " +
                $"HTTP or HTTPS URL, so the in-process server cannot step aside for it. " +
                $"Fix '{ProtoApplication.SectionPath}:{Name}:BaseUrl'.");
        }

        var clientOptions = new WebApplicationFactoryClientOptions();
        _configureClientOptions?.Invoke(clientOptions);
        if (clientOptions.BaseAddress != new Uri("http://localhost"))
        {
            baseAddress = clientOptions.BaseAddress;
        }

        var handlers = CreateClientHandlers(clientOptions)
            .Append(new ProtoTraceContextHandler())
            .ToArray();
        context.RegisterClient(CreateSocketClient(baseAddress, handlers), Name);
        context.Trace.WriteEvent(
            "aspnetcore.server.skipped",
            $"ASP.NET Core server · {Name} runs at {baseAddress}",
            "ProtoTest.AspNetCore",
            ProtoTracePhase.Setup,
            ProtoTraceOutcome.Skipped,
            new Dictionary<string, string?>
            {
                ["aspnetcore.application"] = Name,
                ["aspnetcore.mode"] = "published",
                ["aspnetcore.address"] = baseAddress.ToString(),
                ["aspnetcore.reason"] = $"'{ProtoApplication.SectionPath}:{Name}:BaseUrl' is configured"
            });
    }

    // The socket path mirrors what WebApplicationFactory does for the in-process path: the framework's
    // redirect and cookie behavior, then the context propagator, over the real transport.
    private static HttpClient CreateSocketClient(Uri baseAddress, IReadOnlyList<DelegatingHandler> handlers)
    {
        HttpMessageHandler handler = new HttpClientHandler();
        for (var index = handlers.Count - 1; index >= 0; index--)
        {
            handlers[index].InnerHandler = handler;
            handler = handlers[index];
        }

        return new HttpClient(handler, disposeHandler: true) { BaseAddress = baseAddress };
    }

    /// <summary>
    /// Records the application's page-like routes as <c>web.page.available</c> observations once per run,
    /// so the web coverage report can show pages that exist but were never visited. Published applications
    /// never start in-process, so their inventory comes from <c>ProtoTest:Web:Pages</c> instead. An empty
    /// discovery does not latch: endpoints that appear later (a route added after the first test) still
    /// get inventoried.
    /// </summary>
    private void RecordPageInventory(ProtoExecutionContext context, AspNetCoreServer<TProgram> server)
    {
        if (Volatile.Read(ref _pageInventoryRecorded) != 0) return;
        try
        {
            var recorded = WebPageInventory.Record(
                context,
                "Web",
                AspNetCorePageInventory.Discover(server.Factory.Services, context.Configuration, Name),
                "aspnetcore",
                new Dictionary<string, object> { ["web.application"] = Name });
            if (recorded > 0)
            {
                Interlocked.Exchange(ref _pageInventoryRecorded, 1);
            }
        }
        catch (Exception exception)
        {
            // A failed inventory stays unrecorded, so a later test can still contribute it.
            Interlocked.Exchange(ref _pageInventoryRecorded, 0);
            context.Trace.WriteEvent(
                "web.page.inventory.failed",
                "ASP.NET Core page inventory failed",
                "ProtoTest.AspNetCore",
                outcome: ProtoTraceOutcome.Unknown,
                exception: exception);
        }
    }

    public ValueTask DisposeAsync() => _serverLifetime.DisposeAsync();

    /// <summary>
    /// Started infrastructure provides its connection strings as host settings, so an in-process
    /// application reads the same values the tests do; explicit user configuration still wins because
    /// it is applied afterwards. The application's <see cref="TimeProvider"/> is replaced with the run's
    /// clock bridge too, so application code sees the active test's clock; a suite that registers its
    /// own inside <c>configureWebHost</c> runs later and wins.
    /// </summary>
    private Action<IWebHostBuilder>? CombinedConfigure(ProtoExecutionContext context)
    {
        var settings = context.TryService<ProtoInfrastructureSettings>()?.Values;
        var clock = context.TryService<TimeProvider>();
        var clockRegistry = context.TryService<ProtoClockRegistry>();
        if ((settings is null || settings.Count == 0) && clock is null && _configureWebHost is null)
        {
            return null;
        }

        return webHost =>
        {
            if (settings is not null)
            {
                foreach (var (key, value) in settings)
                {
                    webHost.UseSetting(key, value);
                }
            }

            if (clock is not null)
            {
                webHost.ConfigureTestServices(services =>
                {
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton(clock);
                    // The request flow has no test context of its own: this filter pushes the clock of
                    // the test that sent the request for the duration of the application's handling.
                    // The registry is the owning host's, so the lookup cannot steal another host's clock.
                    if (clockRegistry is not null)
                    {
                        services.AddSingleton<IStartupFilter>(new ProtoClockStartupFilter(clockRegistry));
                    }
                });
            }

            _configureWebHost?.Invoke(webHost);
        };
    }

    /// <summary>
    /// Mirrors <see cref="WebApplicationFactoryClientOptions"/>'s internal handler list so the client keeps
    /// the framework's redirect and cookie behavior while ProtoTest appends its own context propagator.
    /// </summary>
    private static IEnumerable<DelegatingHandler> CreateClientHandlers(WebApplicationFactoryClientOptions options)
    {
        if (options.AllowAutoRedirect)
        {
            yield return new RedirectHandler(options.MaxAutomaticRedirections);
        }

        if (options.HandleCookies)
        {
            yield return new CookieContainerHandler();
        }
    }

    /// <summary>Builds the registration name of the per-client <see cref="WebApplicationFactory{TEntryPoint}"/>.</summary>
    internal static string FactoryName(string name) => $"{name}:Factory";
}

/// <summary>Owns a started <see cref="WebApplicationFactory{TEntryPoint}"/> and everything derived from it.</summary>
internal sealed class AspNetCoreServer<TProgram> : IAsyncDisposable where TProgram : class
{
    private readonly WebApplicationFactory<TProgram> _root;

    private AspNetCoreServer(WebApplicationFactory<TProgram> root, WebApplicationFactory<TProgram> factory)
    {
        _root = root;
        Factory = factory;
    }

    public WebApplicationFactory<TProgram> Factory { get; }

    public static AspNetCoreServer<TProgram> Start(Action<IWebHostBuilder>? configureWebHost)
    {
        var root = new WebApplicationFactory<TProgram>();
        try
        {
            var factory = configureWebHost is null ? root : root.WithWebHostBuilder(configureWebHost);

            // Start the host eagerly so concurrent tests never race WebApplicationFactory's lazy startup.
            _ = factory.Services;
            return new AspNetCoreServer<TProgram>(root, factory);
        }
        catch
        {
            root.Dispose();
            throw;
        }
    }

    // Disposing the root factory also disposes factories derived from it with WithWebHostBuilder.
    public ValueTask DisposeAsync() => _root.DisposeAsync();
}
