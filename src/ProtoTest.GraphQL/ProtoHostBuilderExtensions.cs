namespace ProtoTest.GraphQL;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using ProtoTest.Core;
using ProtoTest.GraphQL.Internal;

public static class ProtoHostBuilderExtensions
{
    public static IProtoHostBuilder AddGraphQL(
        this IProtoHostBuilder builder,
        Action<ProtoGraphQLBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ConfigureServices(services =>
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IProtoTestHook, GraphQLLifecycleHook>());
            services.TryAddSingleton(sp =>
            {
                var options = new GraphQLResponseOptions();
                options.Bind(sp.GetRequiredService<IConfiguration>());
                return options;
            });
        });
        if (configure is not null)
        {
            builder.ConfigureServices(services => configure(new ProtoGraphQLBuilder(services)));
        }
        return builder;
    }
}
