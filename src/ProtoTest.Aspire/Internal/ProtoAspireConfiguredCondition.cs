namespace ProtoTest.Aspire.Internal;

using ProtoTest.Core;

/// <summary>
/// Holds when every key the AppHost's publish mappings fill already has a configured value: the
/// environment provides those addresses, so starting the AppHost would shadow them. It reads the
/// piece's mappings when the chain resolves, after any <c>MapConnectionString</c> mapping added
/// after registration, so the registration uses this condition instead of the target's static key
/// list.
/// </summary>
internal sealed class ProtoAspireConfiguredCondition(IProtoAspireAppHost appHost) : IProtoProviderCondition
{
    public bool IsSatisfied(ProtoProviderConditionContext context)
    {
        var keys = appHost.PublishKeys;
        return keys.Count > 0 && keys.All(key => !string.IsNullOrWhiteSpace(context.Configuration[key]));
    }

    public string Describe(ProtoProviderConditionContext context)
    {
        var keys = appHost.PublishKeys;
        if (keys.Count == 0)
        {
            return "The AppHost fills no configuration key";
        }

        var missing = keys
            .Where(key => string.IsNullOrWhiteSpace(context.Configuration[key]))
            .ToArray();
        return missing.Length == 0
            ? "Every key the AppHost fills has a configured value"
            : $"Every key the AppHost fills must have a configured value (missing: {string.Join(", ", missing)})";
    }
}
