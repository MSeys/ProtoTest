namespace ProtoTest.AspNetCore.Internal;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using ProtoTest.Core;

/// <summary>
/// Initializes an in-process ASP.NET Core test server and a per-test client for it.
/// </summary>
/// <typeparam name="TProgram">The entry point class of the ASP.NET Core application under test.</typeparam>
internal sealed class AspNetCoreClientInitializer<TProgram> : IProtoClientInitializer<HttpClient>, IAsyncDisposable
    where TProgram : class
{
    private readonly Action<IWebHostBuilder>? _configureWebHost;
    private readonly Action<WebApplicationFactoryClientOptions>? _configureClientOptions;
    private readonly AspNetCoreServerLifetime _lifetime;
    private readonly ProtoLock _gate = new();
    private AspNetCoreServer<TProgram>? _sharedServer;

    public AspNetCoreClientInitializer(
        string name,
        Action<IWebHostBuilder>? configureWebHost,
        Action<WebApplicationFactoryClientOptions>? configureClientOptions,
        AspNetCoreServerLifetime lifetime)
    {
        Name = name;
        _configureWebHost = configureWebHost;
        _configureClientOptions = configureClientOptions;
        _lifetime = lifetime;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public Task<bool> TryInitializeAsync(ProtoExecutionContext context, CancellationToken cancellationToken = default)
    {
        var reused = false;
        AspNetCoreServer<TProgram> server;
        if (_lifetime == AspNetCoreServerLifetime.PerRun)
        {
            lock (_gate)
            {
                reused = _sharedServer is not null;
                server = _sharedServer ??= AspNetCoreServer<TProgram>.Start(CombinedConfigure(context));
            }

            context.RegisterClient(server.Factory, FactoryName(Name), disposeWithContext: false);
        }
        else
        {
            server = AspNetCoreServer<TProgram>.Start(CombinedConfigure(context));
            context.RegisterClient(server, FactoryName(Name));
            context.RegisterClient(server.Factory, FactoryName(Name), disposeWithContext: false);
        }

        var clientOptions = new WebApplicationFactoryClientOptions();
        _configureClientOptions?.Invoke(clientOptions);
        var handlers = CreateClientHandlers(clientOptions)
            .Append(new ProtoTraceContextHandler())
            .ToArray();
        var client = server.Factory.CreateDefaultClient(clientOptions.BaseAddress, handlers);
        context.RegisterClient(client, Name);
        var serverState = new Dictionary<string, string?>
        {
            ["application.type"] = typeof(TProgram).FullName,
            ["server.lifetime"] = _lifetime.ToString(),
            ["server.reused"] = reused ? "true" : "false",
            ["web_host.customized"] = (_configureWebHost is not null).ToString().ToLowerInvariant(),
            ["client.customized"] = (_configureClientOptions is not null).ToString().ToLowerInvariant()
        };
        context.Trace.SetEntityState(
            ProtoTraceEntityKinds.Server,
            typeof(TProgram).FullName!,
            $"Server · {typeof(TProgram).Name}",
            serverState,
            scope: context.TestName,
            change: "initialized");
        context.Trace.WriteEvent(
            "aspnetcore.server.initialize",
            $"ASP.NET Core server · {Name}",
            "ProtoTest.AspNetCore",
            ProtoTracePhase.Setup,
            ProtoTraceOutcome.Succeeded,
            serverState,
            entityKind: ProtoTraceEntityKinds.Server,
            entityId: typeof(TProgram).FullName);
        return Task.FromResult(true);
    }

    public ValueTask DisposeAsync()
        => _sharedServer is not null ? _sharedServer.DisposeAsync() : ValueTask.CompletedTask;

    /// <summary>
    /// Started infrastructure provides its connection strings as host settings, so an in-process
    /// application reads the same values the tests do; explicit user configuration still wins because
    /// it is applied afterwards.
    /// </summary>
    private Action<IWebHostBuilder>? CombinedConfigure(ProtoExecutionContext context)
    {
        var settings = context.TryService<ProtoInfrastructureSettings>()?.Values;
        if (settings is null || settings.Count == 0)
        {
            return _configureWebHost;
        }

        return webHost =>
        {
            foreach (var (key, value) in settings)
            {
                webHost.UseSetting(key, value);
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
