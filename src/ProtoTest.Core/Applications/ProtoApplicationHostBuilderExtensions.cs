namespace ProtoTest.Core;

using ProtoTest.Core.Internal;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public static class ProtoApplicationHostBuilderExtensions
{
    /// <summary>
    /// Declares an application under test and the protocols/clients it exposes. A test selects it with
    /// <c>[Application(name)]</c> and then uses the protocol accessors without naming a client.
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
            configure(new ProtoApplicationBuilder(applicationName, services, clients));
            services.AddSingleton(new ProtoApplicationClients(applicationName, clients));
            services.TryAddSingleton<ProtoApplicationRegistry>();
        });
    }
}
