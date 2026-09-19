namespace ProtoTest.AspNetCore;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoTest.AspNetCore.Internal;
using ProtoTest.Core;
using System.Runtime.CompilerServices;

/// <summary>
/// Provides extension methods for <see cref="IProtoHostBuilder"/> to configure ASP.NET Core test hosts.
/// </summary>
public static class ProtoHostBuilderExtensions
{
    // First registration wins per server name: a helper invoked twice cannot start the same server twice,
    // while a different name still composes. The marker is added only after the registration succeeded.
    private static readonly ConditionalWeakTable<IProtoHostBuilder, HashSet<string>> RegisteredServers = new();

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

        var serverNames = RegisteredServers.GetValue(builder, static _ => new HashSet<string>(StringComparer.Ordinal));
        if (serverNames.Contains(name))
        {
            return builder;
        }

        // Registered through a factory so the host's service provider disposes the shared server with the run.
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IProtoClientInitializer>(_ =>
                new AspNetCoreClientInitializer<TProgram>(name, configureWebHost, configureClientOptions, lifetime));
            RegisterApplicationServices<TProgram>(services, name);
        });
        // The name is claimed only once the registration succeeded, so a failed call leaves no guard.
        serverNames.Add(name);
        return builder.AddCapability(new ProtoCapabilityDescriptor(
            "ASP.NET Core", ProtoCapabilityKinds.Server, "ProtoTest.AspNetCore"));
    }

    /// <summary>
    /// Backs an application with an in-process ASP.NET Core server, registered under the application
    /// name so the application's HTTP clients can reuse its transport (for example
    /// <c>rest.AddClientFrom("Api", app.Name)</c>).
    /// </summary>
    /// <remarks>
    /// Repeating the call registers each server and lets the client initializer hook pick the first that
    /// initializes, exactly like re-registering a REST client name; the transport, keyed application
    /// services and capability stay registered once. There is no whole-call guard, so nothing survives a
    /// failure.
    /// </remarks>
    public static IProtoApplicationBuilder AddAspNetCoreServer<TProgram>(
        this IProtoApplicationBuilder application,
        Action<IWebHostBuilder>? configureWebHost = null,
        Action<WebApplicationFactoryClientOptions>? configureClientOptions = null,
        AspNetCoreServerLifetime lifetime = AspNetCoreServerLifetime.PerRun) where TProgram : class
    {
        ArgumentNullException.ThrowIfNull(application);
        if (!Enum.IsDefined(lifetime)) throw new ArgumentOutOfRangeException(nameof(lifetime));

        var name = application.ApplicationName;
        application.Services.AddSingleton<IProtoClientInitializer>(_ =>
            new AspNetCoreClientInitializer<TProgram>(name, configureWebHost, configureClientOptions, lifetime));
        // The application's HTTP clients with no configured base reuse this transport.
        application.Services.TryAddSingleton(new ProtoApplicationTransport(name, name));
        RegisterApplicationServices<TProgram>(application.Services, name);
        return application.AddCapability(new ProtoCapabilityDescriptor(
            "ASP.NET Core", ProtoCapabilityKinds.Server, "ProtoTest.AspNetCore"));
    }

    /// <summary>
    /// Registers the per-test scope over the application's container under the server's name, so
    /// <c>ApplicationServices&lt;TProgram&gt;</c> can reach scoped domain services.
    /// </summary>
    private static void RegisterApplicationServices<TProgram>(IServiceCollection services, string name)
        where TProgram : class
        => services.TryAddKeyedScoped(
            name,
            (_, key) => ApplicationServicesScope<TProgram>.Create(Proto.Context, (string)key!));
}
