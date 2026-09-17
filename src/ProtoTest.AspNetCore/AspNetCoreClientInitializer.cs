namespace ProtoTest.AspNetCore;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using ProtoTest.Core;

/// <summary>
/// Initializes an in-process ASP.NET Core test server and a per-test client for it.
/// </summary>
/// <typeparam name="TProgram">The entry point class of the ASP.NET Core application under test.</typeparam>
internal sealed class AspNetCoreClientInitializer<TProgram> : IProtoClientInitializer<HttpClient>, IAsyncDisposable
    where TProgram : class
{
    private readonly Action<IWebHostBuilder>? _configureWebHost;
    private readonly Action<WebApplicationFactoryClientOptions>? _configureClient;
    private readonly AspNetCoreServerLifetime _lifetime;
    private readonly Lazy<AspNetCoreServer<TProgram>> _sharedServer;

    public AspNetCoreClientInitializer(
        string name,
        Action<IWebHostBuilder>? configureWebHost,
        Action<WebApplicationFactoryClientOptions>? configureClient,
        AspNetCoreServerLifetime lifetime)
    {
        Name = name;
        _configureWebHost = configureWebHost;
        _configureClient = configureClient;
        _lifetime = lifetime;
        _sharedServer = new Lazy<AspNetCoreServer<TProgram>>(
            () => AspNetCoreServer<TProgram>.Start(_configureWebHost),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public Task<bool> TryInitializeAsync(ProtoExecutionContext context, CancellationToken cancellationToken = default)
    {
        var reused = _lifetime == AspNetCoreServerLifetime.PerRun && _sharedServer.IsValueCreated;
        AspNetCoreServer<TProgram> server;
        if (_lifetime == AspNetCoreServerLifetime.PerRun)
        {
            server = _sharedServer.Value;
            context.RegisterClient(server.Factory, FactoryName, disposeWithContext: false);
        }
        else
        {
            server = AspNetCoreServer<TProgram>.Start(_configureWebHost);
            context.RegisterClient(server, FactoryName);
            context.RegisterClient(server.Factory, FactoryName, disposeWithContext: false);
        }

        var clientOptions = new WebApplicationFactoryClientOptions();
        _configureClient?.Invoke(clientOptions);
        context.RegisterClient(server.Factory.CreateClient(clientOptions), Name);
        context.Trace.WriteEvent(
            "aspnetcore.server.initialize",
            $"ASP.NET Core server · {Name}",
            "ProtoTest.AspNetCore",
            ProtoTracePhase.Setup,
            ProtoTraceOutcome.Succeeded,
            new Dictionary<string, string?>
            {
                ["client.name"] = Name,
                ["application.type"] = typeof(TProgram).FullName,
                ["server.lifetime"] = _lifetime.ToString(),
                ["server.reused"] = reused ? "true" : "false",
                ["web_host.customized"] = (_configureWebHost is not null).ToString().ToLowerInvariant(),
                ["client.customized"] = (_configureClient is not null).ToString().ToLowerInvariant()
            });
        return Task.FromResult(true);
    }

    public ValueTask DisposeAsync()
        => _sharedServer.IsValueCreated ? _sharedServer.Value.DisposeAsync() : ValueTask.CompletedTask;

    private string FactoryName => $"{Name}:Factory";
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
