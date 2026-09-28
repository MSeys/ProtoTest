namespace ProtoTest.Core;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoTest.Core.Internal;

public static class ProtoApplicationHostBuilderExtensions
{
    /// <summary>
    /// Declares an application under test and the protocols/clients it exposes. A test selects it with
    /// <c>[Application(name)]</c> and then uses the protocol accessors without naming a client. Add the
    /// application's providers (<c>UseConfigured()</c>, <c>UseInProcess&lt;TProgram&gt;()</c>, an
    /// integration's AppHost resource) in priority order to resolve how the run serves it; without any
    /// provider the application keeps the plain configured-address behavior.
    /// </summary>
    public static IProtoHostBuilder AddApplication(
        this IProtoHostBuilder builder,
        string applicationName,
        Action<IProtoApplicationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        ArgumentNullException.ThrowIfNull(configure);

        return builder.ConfigureServices(services =>
        {
            var clients = new List<ProtoApplicationClient>();
            var application = new ProtoApplicationBuilder(
                applicationName,
                builder,
                services,
                clients,
                builder is ProtoHostBuilder hostBuilder ? () => hostBuilder.IsBuilt : null);
            configure(application);
            services.AddSingleton(new ProtoApplicationClients(applicationName, clients));
            services.TryAddSingleton<ProtoApplicationRegistry>();
            application.RegisterTarget();
        });
    }

    /// <summary>
    /// Adds the provider that serves the application from its configured address: it holds when every
    /// key the application declares - its derived <c>ProtoTest:Applications:{name}:BaseUrl</c> - has a
    /// value, and it starts nothing. Put it first in a chain that should prefer the environment over
    /// anything the run could start.
    /// </summary>
    public static IProtoApplicationBuilder UseConfigured(this IProtoApplicationBuilder application)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.Providers.UseConfigured();
        return application;
    }
}
