namespace ProtoTest.AspNetCore;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
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
    /// <param name="configureFactory">Optional callback to configure the underlying <see cref="WebApplicationFactory{TEntryPoint}"/>.</param>
    /// <returns>The modified <see cref="IProtoHostBuilder"/>.</returns>
    public static IProtoHostBuilder AddAspNetCoreServer<TProgram>(
        this IProtoHostBuilder builder,
        string name = "Default",
        Action<WebApplicationFactory<TProgram>>? configureFactory = null) where TProgram : class
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.ConfigureServices(services =>
        {
            services.AddSingleton<IProtoClientInitializer>(sp =>
            {
                var configuration = sp.GetRequiredService<IConfiguration>();
                return new AspNetCoreClientInitializer<TProgram>(name, configuration, configureFactory);
            });
        });
    }
}