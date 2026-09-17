namespace ProtoTest.AspNetCore;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

/// <summary>
/// Provides extension methods for <see cref="IProtoHostBuilder"/> to configure ASP.NET Core test hosts.
/// </summary>
public static class ProtoHostBuilderExtensions
{
    /// <summary>
    /// Registers an ASP.NET Core application under test into the ProtoTest execution pipeline.
    /// </summary>
    /// <typeparam name="TProgram">The entry point class of the ASP.NET Core application under test.</typeparam>
    /// <param name="builder">The <see cref="IProtoHostBuilder"/> instance.</param>
    /// <param name="name">The unique identifier for this server instance. Defaults to "Default".</param>
    /// <param name="configureWebHost">Optional callback for replacing services or changing the in-process web host.</param>
    /// <param name="configureClient">Optional callback for configuring the generated HTTP client.</param>
    /// <param name="lifetime">
    /// Whether one application instance serves the whole run (the default) or each test starts its own.
    /// </param>
    /// <returns>The modified <see cref="IProtoHostBuilder"/>.</returns>
    public static IProtoHostBuilder AddAspNetCoreServer<TProgram>(
        this IProtoHostBuilder builder,
        string name = "Default",
        Action<IWebHostBuilder>? configureWebHost = null,
        Action<WebApplicationFactoryClientOptions>? configureClient = null,
        AspNetCoreServerLifetime lifetime = AspNetCoreServerLifetime.PerRun) where TProgram : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Enum.IsDefined(lifetime)) throw new ArgumentOutOfRangeException(nameof(lifetime));

        // Registered through a factory so the host's service provider disposes the shared server with the run.
        return builder.ConfigureServices(services =>
            services.AddSingleton<IProtoClientInitializer>(_ =>
                new AspNetCoreClientInitializer<TProgram>(name, configureWebHost, configureClient, lifetime)));
    }
}
