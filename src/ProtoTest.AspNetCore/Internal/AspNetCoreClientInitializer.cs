namespace ProtoTest.AspNetCore.Internal;

using System.Runtime.ExceptionServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using ProtoTest.Core;
using ProtoTest.Web.Pages;

/// <summary>
/// Initializes an in-process ASP.NET Core test server and a per-test client for it, and serves the run
/// itself before its first test through the same server lifetime.
/// </summary>
/// <typeparam name="TProgram">The entry point class of the ASP.NET Core application under test.</typeparam>
internal sealed class AspNetCoreClientInitializer<TProgram> : IProtoClientInitializer<HttpClient>, IAspNetCoreSubstitutionTarget, IProtoRunApplicationTransport, IAsyncDisposable
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

    string IAspNetCoreSubstitutionTarget.ServerName => Name;

    string IProtoRunApplicationTransport.ApplicationName => Name;

    /// <summary>
    /// A client for the run's own read, built like a test's client without a test: a per-run server is
    /// the one the tests then reuse, a per-test lifetime starts a server the run releases after the read.
    /// </summary>
    ValueTask<ProtoRunApplicationClient> IProtoRunApplicationTransport.OpenRunClientAsync(ProtoRunSetupContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var configure = CombinedConfigure(
            context.Settings.Values,
            context.Services.GetService<TimeProvider>(),
            context.Services.GetService<ProtoClockRegistry>());
        var (server, owned) = _serverLifetime.AcquireForRun(configure);
        try
        {
            var clientOptions = new WebApplicationFactoryClientOptions();
            _configureClientOptions?.Invoke(clientOptions);
            var client = CreateClient(server.Server.CreateHandler(), clientOptions.BaseAddress, [.. CreateClientHandlers(clientOptions)]);
            return ValueTask.FromResult(new ProtoRunApplicationClient(client, owned ? server : null));
        }
        catch
        {
            if (owned)
            {
                server.Dispose();
            }

            throw;
        }
    }

    /// <summary>
    /// Serves the test from a dedicated server built with its accumulated substitutions. The run's
    /// shared server is never reconfigured: a substituting test under either lifetime gets its own
    /// server, built after its substitutions are known and released with the test, so one test's
    /// override cannot leak into the next and parallel tests that substitute differently never meet.
    /// </summary>
    void IAspNetCoreSubstitutionTarget.ApplySubstitution(
        ProtoExecutionContext context,
        ServiceSubstitution substitution,
        ProtoTracePhase phase)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(substitution);

        var address = ProtoApplication.BaseUrl(context.Configuration, Name);
        if (!string.IsNullOrWhiteSpace(address))
        {
            throw new InvalidOperationException(
                $"Application '{Name}' runs at '{address}', so its services cannot be substituted. " +
                $"Substitution needs the in-process server: run without a configured " +
                $"'{ProtoApplication.SectionPath}:{Name}:BaseUrl'.");
        }

        var union = AspNetCoreSubstitutionLedger.Append(context, substitution);
        if (union is null)
        {
            return;
        }

        var entityId = $"server:{typeof(TProgram).FullName}:{Name}";
        using var operation = context.Trace
            .Operation(
                substitution.OperationKind,
                $"{substitution.OperationKind} · {substitution.ServiceType.Name}",
                "ProtoTest.AspNetCore")
            .During(phase)
            .For(ProtoTraceEntityKinds.Server, entityId)
            .With("service.type", substitution.ServiceType.FullName)
            .With("service.server", Name)
            .With("service.replacement", substitution.Replacement)
            .Begin();
        try
        {
            // The substitution runs after the suite's own web-host callback, so the test's
            // replacement wins over the registration the suite composed.
            var server = AspNetCoreServer<TProgram>.Create(CombinedConfigureWithSubstitutions(context, union));
            try
            {
                // The dedicated server is owned before it starts: a registration that throws once the
                // test is releasing (or a racing override) leaves the server to teardown instead of
                // leaking a started server. A registration that did not take ownership disposes the
                // unstarted server here.
                if (context.TryClient<AspNetCoreServer<TProgram>>(FactoryName(Name)) is null)
                {
                    context.RegisterClient(server, FactoryName(Name));
                }
                else
                {
                    context.ReplaceClient(server, FactoryName(Name));
                }
            }
            catch
            {
                server.Dispose();
                throw;
            }

            server.Start();
            context.ReplaceClient(server.Factory, FactoryName(Name), ProtoClientOwnership.Caller);
            var clientOptions = new WebApplicationFactoryClientOptions();
            _configureClientOptions?.Invoke(clientOptions);
            var handlers = CreateClientHandlers(clientOptions)
                .Append(new ProtoTraceContextHandler())
                .ToArray();
            var client = CreateClient(server.Server.CreateHandler(), clientOptions.BaseAddress, handlers);
            context.ReplaceClient(client, Name);
            ReportSubstitutedServer(context, union, entityId);
            AspNetCoreSubstitutionLedger.MarkApplied(context, Name, union);
            operation.Succeed();
        }
        catch (Exception exception)
        {
            // The union stays unapplied, so a retry rebuilds instead of skipping a server that never
            // started.
            operation.Fail(exception);
            throw;
        }
    }

    /// <summary>
    /// Records the dedicated server the same way the shared one is recorded: an initialize event and
    /// the server entity state, carrying which services the test substituted.
    /// </summary>
    private void ReportSubstitutedServer(
        ProtoExecutionContext context,
        IReadOnlyList<ServiceSubstitution> union,
        string entityId)
    {
        var serverState = new AspNetCoreServerState(
            Name,
            typeof(TProgram).FullName!,
            typeof(TProgram).Name,
            _serverLifetime.Kind,
            Reused: false,
            WebHostCustomized: true,
            ClientCustomized: _configureClientOptions is not null,
            Substitutions: [.. union.Select(record => record.ServiceType.FullName!)]);
        context.Trace.SetEntityState(
            ProtoTraceEntityKinds.Server,
            entityId,
            serverState.DisplayName,
            serverState.ToAttributes(),
            scope: context.TestName,
            change: "substituted");
        context.Trace.WriteEvent(
            "aspnetcore.server.initialize",
            $"ASP.NET Core server · {Name}",
            "ProtoTest.AspNetCore",
            ProtoTracePhase.Setup,
            ProtoTraceOutcome.Succeeded,
            serverState.ToAttributes(),
            entityKind: ProtoTraceEntityKinds.Server,
            entityId: entityId);
    }

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
        // application's HTTP clients instead. HTTP clients read static configuration only: an address a
        // started piece published belongs to the sessions that drive that process.
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
        // The client is built over the server's transport handler and owned by the test context alone.
        // The factory's own client ledger is shared by every test of a per-run server and is not safe
        // to mutate in parallel; factory disposal enumerates it, so a torn entry crashes teardown.
        var client = CreateClient(server.Server.CreateHandler(), clientOptions.BaseAddress, handlers);
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
        context.RegisterClient(CreateClient(new HttpClientHandler(), baseAddress, handlers), Name);
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

    // The in-process and the socket path chain ProtoTest's handlers over a transport handler the same
    // way: the framework's redirect and cookie behavior, then the context propagator, innermost. The
    // transport handler comes from the TestServer or from a real socket.
    private static HttpClient CreateClient(
        HttpMessageHandler transport,
        Uri baseAddress,
        IReadOnlyList<DelegatingHandler> handlers)
    {
        HttpMessageHandler handler = transport;
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
    /// The suite's combined web-host configuration with the test's substitutions appended, so the
    /// replacement wins over the registration the suite composed.
    /// </summary>
    private Action<IWebHostBuilder>? CombinedConfigureWithSubstitutions(
        ProtoExecutionContext context,
        IReadOnlyList<ServiceSubstitution> union)
    {
        var baseConfigure = CombinedConfigure(context);
        return webHost =>
        {
            baseConfigure?.Invoke(webHost);
            webHost.ConfigureTestServices(services =>
            {
                foreach (var substitution in union)
                {
                    substitution.ApplyTo(services);
                }
            });
        };
    }

    /// <summary>
    /// Started infrastructure provides its connection strings as host settings, so an in-process
    /// application reads the same values the tests do; explicit user configuration still wins because
    /// it is applied afterwards. The application's <see cref="TimeProvider"/> is replaced with the run's
    /// clock bridge too, so application code sees the active test's clock; a suite that registers its
    /// own inside <c>configureWebHost</c> runs later and wins.
    /// </summary>
    private Action<IWebHostBuilder>? CombinedConfigure(ProtoExecutionContext context)
        => CombinedConfigure(
            context.TryService<ProtoInfrastructureSettings>()?.Values,
            context.TryService<TimeProvider>(),
            context.TryService<ProtoClockRegistry>());

    private Action<IWebHostBuilder>? CombinedConfigure(
        IReadOnlyDictionary<string, string>? settings,
        TimeProvider? clock,
        ProtoClockRegistry? clockRegistry)
    {
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
    private readonly StartupFailureCapture _startupFailure;

    private AspNetCoreServer(
        WebApplicationFactory<TProgram> root,
        WebApplicationFactory<TProgram> factory,
        StartupFailureCapture startupFailure)
    {
        _root = root;
        Factory = factory;
        _startupFailure = startupFailure;
    }

    public WebApplicationFactory<TProgram> Factory { get; }

    /// <summary>The started test server the client transports run over.</summary>
    public TestServer Server => Factory.Server;

    /// <summary>
    /// Creates the unstarted root and derived factories. The caller owns the returned instance: register
    /// or otherwise own it before calling <see cref="Start"/>, so a later failure cannot leak it.
    /// </summary>
    public static AspNetCoreServer<TProgram> Create(Action<IWebHostBuilder>? configureWebHost)
    {
        var root = new WebApplicationFactory<TProgram>();
        try
        {
            var startupFailure = new StartupFailureCapture();
            var factory = root.WithWebHostBuilder(webHost =>
            {
                configureWebHost?.Invoke(webHost);
                webHost.ConfigureTestServices(services => services.AddSingleton<ILoggerProvider>(startupFailure));
            });
            return new AspNetCoreServer<TProgram>(root, factory, startupFailure);
        }
        catch
        {
            root.Dispose();
            throw;
        }
    }

    /// <summary>Starts the created host eagerly, so concurrent tests never race WebApplicationFactory's lazy startup.</summary>
    public void Start()
    {
        try
        {
            _ = Factory.Services;
        }
        catch (Exception exception) when (_startupFailure.Failure is { } failure && !ReferenceEquals(failure, exception))
        {
            // The host reported why it failed; the factory may only have seen the host it disposed.
            ExceptionDispatchInfo.Throw(failure);
        }
    }

    public static AspNetCoreServer<TProgram> Start(Action<IWebHostBuilder>? configureWebHost)
    {
        var server = Create(configureWebHost);
        try
        {
            server.Start();
            return server;
        }
        catch
        {
            server.Dispose();
            throw;
        }
    }

    /// <summary>Disposes the root factory; safe on an unstarted instance.</summary>
    public void Dispose() => _root.Dispose();

    // Disposing the root factory also disposes factories derived from it with WithWebHostBuilder.
    public ValueTask DisposeAsync() => _root.DisposeAsync();
}
