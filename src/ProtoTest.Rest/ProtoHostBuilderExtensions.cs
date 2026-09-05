namespace ProtoTest.Rest.Extensions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Rest.Internal;

public static class ProtoHostBuilderExtensions
{
    /// <summary>
    /// Registers usage of ProtoTest.Reset.
    /// </summary>
    public static IProtoHostBuilder AddRest(this IProtoHostBuilder builder)
    {
        builder.AddHook<RestLifecycleHook>();
        return builder;
    }

    /// <summary>
    /// Registers a REST client initializer
    /// </summary>
    public static IProtoHostBuilder AddRestClient(
        this IProtoHostBuilder builder,
        string name = "Default",
        string? baseUrl = null)
    {
        builder.AddHook<RestLifecycleHook>();
        return builder.ConfigureServices(services =>
        {
            services.AddSingleton<IProtoClientInitializer>(sp =>
            {
                var configuration = sp.GetRequiredService<IConfiguration>();
                return new GenericRestClientInitializer(name, baseUrl);
            });
        });
    }
}