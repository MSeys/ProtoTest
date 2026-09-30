namespace ProtoTest.Core;

using System.Runtime.CompilerServices;
using ProtoTest.Core.Internal;

/// <summary>Registers a suite's additional sensitive names for the run.</summary>
public static class ProtoRedactionExtensions
{
    private static readonly ConditionalWeakTable<IProtoHostBuilder, ProtoRedactionOptions> Options = new();

    /// <summary>
    /// The one <see cref="ProtoRedactionOptions"/> instance for a builder: every redaction consumer
    /// of the host resolves through it, so one list governs state values and finding metadata.
    /// </summary>
    internal static ProtoRedactionOptions ResolveOptions(IProtoHostBuilder builder)
        => Options.GetValue(builder, static _ => new ProtoRedactionOptions());

    /// <summary>
    /// Names property values the run redacts on top of the defaults, such as a domain token the
    /// shared list does not know. The names travel with the host, so a second host in the same
    /// process keeps the defaults only. The section <c>ProtoTest:Redaction</c> binds over the code
    /// values when the host is built.
    /// </summary>
    public static IProtoHostBuilder ConfigureRedaction(
        this IProtoHostBuilder builder,
        Action<ProtoRedactionOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        // The options instance is builder state that the built host already resolved: a late change
        // would mutate the live policy instead of the composition, so this entry takes the terminal
        // rule along with every other composition entry.
        if (builder is IProtoComposableBuilder composable)
        {
            composable.ThrowIfBuilt();
        }

        configure(ResolveOptions(builder));
        return builder;
    }
}
