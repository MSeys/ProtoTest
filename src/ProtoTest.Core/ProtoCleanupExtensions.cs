namespace ProtoTest.Core;

using ProtoTest.Core.Internal;

/// <summary>Selects what a failing cleanup does to the test the runner reports.</summary>
public static class ProtoCleanupExtensions
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IProtoHostBuilder, ProtoCleanupOptions> Options = new();

    /// <summary>
    /// The one <see cref="ProtoCleanupOptions"/> instance for a builder. The scope and the lifecycle
    /// both read it, so a test and its trace follow the same rule.
    /// </summary>
    internal static ProtoCleanupOptions ResolveOptions(IProtoHostBuilder builder)
        => Options.GetValue(builder, static _ => new ProtoCleanupOptions());

    /// <summary>
    /// Chooses whether a failing cleanup fails the test or is only recorded. The default is
    /// <see cref="ProtoCleanupFailureMode.Fail"/>. The section <c>ProtoTest</c> binds over the code
    /// values when the host is built, so <c>ProtoTest:CleanupFailures</c> can select <c>Report</c>.
    /// </summary>
    public static IProtoHostBuilder ConfigureCleanup(
        this IProtoHostBuilder builder,
        Action<ProtoCleanupOptions> configure)
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
