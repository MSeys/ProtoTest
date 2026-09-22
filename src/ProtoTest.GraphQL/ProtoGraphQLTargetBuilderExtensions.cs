namespace ProtoTest.GraphQL;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

public static class ProtoGraphQLTargetBuilderExtensions
{
    /// <summary>
    /// Selects the transport a target's subscriptions use, unless one was already selected for it: the
    /// first registration wins, matching how collectors and messaging adapters treat repeats.
    /// </summary>
    public static IProtoTargetBuilder WithSubscriptionTransport(
        this IProtoTargetBuilder target,
        GraphQLSubscriptionTransport transport)
    {
        ArgumentNullException.ThrowIfNull(target);
        var marker = new GraphQLSubscriptionTransportRegistration(target.TargetName, transport);
        ProtoRegistration.TryAdd(
            target.Services,
            marker,
            existing => string.Equals(existing.TargetName, marker.TargetName, StringComparison.OrdinalIgnoreCase));
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
