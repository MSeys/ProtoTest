namespace ProtoTest.Sql.Internal;

using ProtoTest.Core;

/// <summary>
/// Whether the run can provide a connection for the keys <see cref="SqlOptions.AddressKeys"/> declares,
/// and the failure the accessors raise when it cannot. The same predicate decides the Store capability
/// at <c>Build</c>: a key counts as provided when it has a configured value, or a registered
/// infrastructure piece declares it and fills it when it starts.
/// </summary>
internal static class SqlAddressRule
{
    /// <summary>Whether at least one declared key is provided for the running test.</summary>
    public static bool AnyProvided(ProtoExecutionContext context, SqlAddressKeys keys)
    {
        var settings = context.TryService<ProtoInfrastructureSettings>()?.Values;
        for (var index = 0; index < keys.Count; index++)
        {
            var key = keys[index];
            if (!string.IsNullOrWhiteSpace(context.Configuration[key]))
            {
                return true;
            }

            if (settings is not null
                && settings.TryGetValue(key, out var value)
                && !string.IsNullOrWhiteSpace(value))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the SQL integration is inert: address keys are declared and none is provided.</summary>
    public static bool IsInert(ProtoExecutionContext context, SqlOptions options)
        => options.AddressKeys.Count > 0 && !AnyProvided(context, options.AddressKeys);

    /// <summary>Throws naming the missing keys and the capability gate when the integration is inert.</summary>
    public static void ThrowIfInert(ProtoExecutionContext context, SqlOptions options)
    {
        if (!IsInert(context, options))
        {
            return;
        }

        throw new InvalidOperationException(
            $"The SQL integration has no connection: none of its address keys ({string.Join(", ", options.AddressKeys.ToArray())}) " +
            "is provided. Configure one of them, or gate the test with [RequiresCapability(ProtoCapabilityKinds.Store)].");
    }
}
