namespace ProtoTest.AspNetCore.Internal;

using Microsoft.AspNetCore.Hosting;
using ProtoTest.Core;
using ProtoTest.Core.Internal;

/// <summary>
/// How one lifetime acquires the application's server. The strategy owns the acquisition and the
/// ownership registrations, so the initializer does not branch on the lifetime.
/// </summary>
internal interface IAspNetCoreServerLifetime<TProgram> : IAsyncDisposable
    where TProgram : class
{
    /// <summary>The lifetime the strategy implements, as the server state reports it.</summary>
    AspNetCoreServerLifetime Kind { get; }

    AspNetCoreServerLease<TProgram> Acquire(
        ProtoExecutionContext context,
        string name,
        Func<ProtoExecutionContext, Action<IWebHostBuilder>?> configure);
}

/// <summary>The server one test runs against, and whether the run already had it.</summary>
internal sealed record AspNetCoreServerLease<TProgram>(AspNetCoreServer<TProgram> Server, bool Reused)
    where TProgram : class;

/// <summary>
/// One server serves every test in the run, started on first use and released with the initializer.
/// The test's caller-owned client is the factory, not the server: the server's life is the run's.
/// </summary>
internal sealed class PerRunServerLifetime<TProgram> : IAspNetCoreServerLifetime<TProgram>
    where TProgram : class
{
    private readonly ProtoLock _gate = new();
    private AspNetCoreServer<TProgram>? _sharedServer;

    public AspNetCoreServerLifetime Kind => AspNetCoreServerLifetime.PerRun;

    public AspNetCoreServerLease<TProgram> Acquire(
        ProtoExecutionContext context,
        string name,
        Func<ProtoExecutionContext, Action<IWebHostBuilder>?> configure)
    {
        AspNetCoreServer<TProgram> server;
        bool reused;
        lock (_gate)
        {
            reused = _sharedServer is not null;
            server = _sharedServer ??= AspNetCoreServer<TProgram>.Start(configure(context));
        }

        context.RegisterClient(
            server.Factory,
            AspNetCoreClientInitializer<TProgram>.FactoryName(name),
            ProtoClientOwnership.Caller);
        return new(server, reused);
    }

    public ValueTask DisposeAsync()
        => _sharedServer is not null ? _sharedServer.DisposeAsync() : ValueTask.CompletedTask;
}

/// <summary>
/// Every test starts its own server and hands it to the test's resources, so the test owns its
/// disposal and no state survives into the next test.
/// </summary>
internal sealed class PerTestServerLifetime<TProgram> : IAspNetCoreServerLifetime<TProgram>
    where TProgram : class
{
    public AspNetCoreServerLifetime Kind => AspNetCoreServerLifetime.PerTest;

    public AspNetCoreServerLease<TProgram> Acquire(
        ProtoExecutionContext context,
        string name,
        Func<ProtoExecutionContext, Action<IWebHostBuilder>?> configure)
    {
        var server = AspNetCoreServer<TProgram>.Start(configure(context));
        context.RegisterClient(server, AspNetCoreClientInitializer<TProgram>.FactoryName(name));
        context.RegisterClient(
            server.Factory,
            AspNetCoreClientInitializer<TProgram>.FactoryName(name),
            ProtoClientOwnership.Caller);
        return new(server, Reused: false);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
