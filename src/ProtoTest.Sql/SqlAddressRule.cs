namespace ProtoTest.Sql;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Sql.Internal;

/// <summary>
/// The SQL address rule: whether the run can provide a connection for the keys
/// <see cref="SqlOptions.AddressKeys"/> declares, and the failure the accessors raise when it cannot.
/// The same predicate decides the Store capability at <c>Build</c>: a key counts as provided when it
/// has a configured value, or a registered infrastructure piece declares it and fills it when it
/// starts. A sibling access technology - Entity Framework Core, Dapper, raw ADO.NET - shares the rule:
/// read <see cref="DeclaredKeys"/> while the host builds to gate its own Store capability, and use
/// <see cref="IsInert"/> or <see cref="ThrowIfInert"/> on the same keys at use time, so the capability
/// decision and the failure text cannot disagree with <c>AddSql</c>.
/// </summary>
public static class SqlAddressRule
{
    /// <summary>
    /// The keys the first successful <c>AddSql</c> declared, in declaration order, or an empty list
    /// when it has not run yet. Read this when a sibling integration is registered - after
    /// <c>AddSql</c> - to declare its Store capability over the same keys.
    /// </summary>
    public static IReadOnlyList<string> DeclaredKeys(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return SqlAddressKeyDeclaration.Find(services)?.Keys ?? [];
    }

    /// <summary>Whether at least one declared key is provided for the running test.</summary>
    public static bool AnyProvided(ProtoExecutionContext context, SqlAddressKeys keys)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(keys);
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
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        return options.AddressKeys.Count > 0 && !AnyProvided(context, options.AddressKeys);
    }

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
