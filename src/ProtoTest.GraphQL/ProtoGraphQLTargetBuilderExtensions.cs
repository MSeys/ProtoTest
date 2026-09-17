namespace ProtoTest.GraphQL;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

public static class ProtoGraphQLTargetBuilderExtensions
{
    public static IProtoTargetBuilder WithSubscriptionTransport(
        this IProtoTargetBuilder target,
        GraphQLSubscriptionTransport transport)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.Services.AddSingleton(new GraphQLSubscriptionTransportRegistration(target.TargetName, transport));
        return target;
    }

    public static IProtoTargetBuilder WithSchemaCoverage(this IProtoTargetBuilder target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.AddCollector<GraphQLSchemaCoverageCollector>();
    }

    public static IProtoTargetBuilder WithSchemaCoverage(this IProtoTargetBuilder target, string schemaSource)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaSource);
        return target.AddCollector<GraphQLSchemaCoverageCollector>(schemaSource);
    }
}
