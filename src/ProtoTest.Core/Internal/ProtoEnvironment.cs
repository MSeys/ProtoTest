namespace ProtoTest.Core.Internal;

using Microsoft.Extensions.Configuration;

/// <summary>Which environment state makes a declaration's configuration keys unnecessary.</summary>
internal enum ProtoEnvironmentMode
{
    /// <summary>Every key has a configured value, so the environment already provides what the declaration would.</summary>
    AllConfigured,

    /// <summary>At least one key is provided - a configured value or a key a registered infrastructure piece declares.</summary>
    AnyProvided
}

/// <summary>
/// The one evaluator behind "does the run's environment satisfy this declaration's configuration keys":
/// the infrastructure-skip decision and the capability-drop decision both read it, so the two key
/// semantics cannot drift apart. A declaration with no keys is never satisfied: the environment has
/// nothing to answer for it.
/// </summary>
internal static class ProtoEnvironment
{
    /// <summary>The shared empty declared-key set for a caller that has no declarations to answer for.</summary>
    public static readonly IReadOnlySet<string> NoDeclaredKeys = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Whether the environment satisfies <paramref name="keys"/> under <paramref name="mode"/>: under
    /// <see cref="ProtoEnvironmentMode.AllConfigured"/> every key has a configured value, under
    /// <see cref="ProtoEnvironmentMode.AnyProvided"/> at least one key is provided.
    /// </summary>
    public static bool IsSatisfied(
        IConfiguration configuration,
        IReadOnlyList<string> keys,
        ProtoEnvironmentMode mode,
        IReadOnlySet<string> declaredKeys)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(declaredKeys);
        if (keys.Count == 0)
        {
            return false;
        }

        return mode switch
        {
            ProtoEnvironmentMode.AllConfigured => keys.All(key => HasValue(configuration, key)),
            ProtoEnvironmentMode.AnyProvided => keys.Any(key => IsProvided(configuration, key, declaredKeys)),
            _ => false
        };
    }

    /// <summary>
    /// Whether a key is provided: configuration has a value for it, or a registered infrastructure
    /// piece declares it - including a piece this build skips because configuration already fills its
    /// keys, so a container that will fill a key counts before it starts.
    /// </summary>
    public static bool IsProvided(IConfiguration configuration, string key, IReadOnlySet<string> declaredKeys)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(declaredKeys);
        return HasValue(configuration, key) || declaredKeys.Contains(key);
    }

    /// <summary>Whether the key has a configured value; the one blank-value rule every condition reads.</summary>
    public static bool HasValue(IConfiguration configuration, string key)
        => !string.IsNullOrWhiteSpace(configuration[key]);
}
