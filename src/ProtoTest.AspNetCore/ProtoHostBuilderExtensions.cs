namespace ProtoTest.AspNetCore;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.AspNetCore.Internal;
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
    /// <param name="configureClientOptions">Optional callback for configuring the generated HTTP client options.</param>
    /// <param name="lifetime">
    /// Whether one application instance serves the whole run (the default) or each test starts its own.
    /// </param>
    /// <returns>The modified <see cref="IProtoHostBuilder"/>.</returns>
    public static IProtoHostBuilder AddAspNetCoreServer<TProgram>(
        this IProtoHostBuilder builder,
        string name = "Default",
        Action<IWebHostBuilder>? configureWebHost = null,
        Action<WebApplicationFactoryClientOptions>? configureClientOptions = null,
        AspNetCoreServerLifetime lifetime = AspNetCoreServerLifetime.PerRun) where TProgram : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Enum.IsDefined(lifetime)) throw new ArgumentOutOfRangeException(nameof(lifetime));

        // Registered through a factory so the host's service provider disposes the shared server with the run.
        return builder.ConfigureServices(services =>
        {
            services.AddSingleton<IProtoClientInitializer>(_ =>
                new AspNetCoreClientInitializer<TProgram>(name, configureWebHost, configureClientOptions, lifetime));
            RegisterApplicationServices<TProgram>(services, name);
        });
    }

    /// <summary>
    /// Backs an application with an in-process ASP.NET Core server, registered under the application
    /// name so the application's HTTP clients can reuse its transport (for example
    /// <c>rest.AddClientFrom("Api", app.Name)</c>).
    /// </summary>
    public static IProtoApplicationBuilder AddAspNetCoreServer<TProgram>(
        this IProtoApplicationBuilder application,
        Action<IWebHostBuilder>? configureWebHost = null,
        Action<WebApplicationFactoryClientOptions>? configureClientOptions = null,
        AspNetCoreServerLifetime lifetime = AspNetCoreServerLifetime.PerRun) where TProgram : class
    {
        ArgumentNullException.ThrowIfNull(application);
        var name = application.ApplicationName;
        application.Services.AddSingleton<IProtoClientInitializer>(_ =>
            new AspNetCoreClientInitializer<TProgram>(name, configureWebHost, configureClientOptions, lifetime));
        // The application's HTTP clients with no configured base reuse this transport.
        application.Services.AddSingleton(new ProtoApplicationTransport(name, name));
        RegisterApplicationServices<TProgram>(application.Services, name);
        return application;
    }

    /// <summary>
    /// Registers the per-test scope over the application's container under the server's name, so
    /// <c>ApplicationServices&lt;TProgram&gt;</c> can reach scoped domain services.
    /// </summary>
    private static void RegisterApplicationServices<TProgram>(IServiceCollection services, string name)
        where TProgram : class
        => services.AddKeyedScoped(
            name,
            (_, key) => ApplicationServicesScope<TProgram>.Create(Proto.Context, (string)key!));
}
