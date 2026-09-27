namespace ProtoTest.Core.Internal;

using Microsoft.Extensions.Configuration;

/// <summary>One registered target: its identity, the keys it declares once and its providers in priority order.</summary>
internal sealed record ProtoTargetChain(
    string Name,
    IReadOnlyList<string> Keys,
    IReadOnlyList<IProtoTargetProvider> Providers)
{
    /// <summary>Targets this chain resolves after, so its conditions can read their winners.</summary>
    public IReadOnlyList<string> ResolveAfter { get; init; } = [];
}

/// <summary>One provider that did not win its target's chain, and the reason it lost.</summary>
internal sealed record ProtoProviderSkip(IProtoTargetProvider Provider, string Reason);

/// <summary>The resolved chain of one target: the winning provider and every skipped one with its reason.</summary>
internal sealed record ProtoTargetResolution(
    string TargetName,
    IReadOnlyList<string> Keys,
    IProtoTargetProvider Winner,
    IReadOnlyList<ProtoProviderSkip> Skipped);

/// <summary>The run's target resolutions, recorded in the trace when the run starts.</summary>
internal sealed record ProtoTargetResolutions(IReadOnlyList<ProtoTargetResolution> Targets);

/// <summary>
/// The one resolver behind ordered target resolution: walk each target's chain in order against the
/// resolved configuration and take the first provider whose condition holds. A provider before the
/// winner is kept with the condition it did not meet; a provider after the winner is kept as lower
/// priority. A chain no provider can serve fails the host's build, naming every candidate.
/// </summary>
internal static class ProtoTargetResolver
{
    /// <summary>Resolves every target's chain, or throws naming the first target no provider can serve.</summary>
    public static IReadOnlyList<ProtoTargetResolution> Resolve(
        IConfiguration configuration,
        IReadOnlyList<ProtoTargetChain> chains)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(chains);
        var resolutions = new List<ProtoTargetResolution>(chains.Count);
        var resolved = new Dictionary<string, ProtoResolvedTarget>(StringComparer.Ordinal);
        foreach (var chain in Order(chains))
        {
            var resolution = ResolveTarget(configuration, chain, resolved);
            resolutions.Add(resolution);
            resolved[resolution.TargetName] = new ProtoResolvedTarget(
                resolution.TargetName,
                resolution.Winner.Name,
                resolution.Winner.Capabilities);
        }

        return resolutions;
    }

    // A chain that declared ResolveAfter waits for the targets it names, so a worker's conditions read
    // the application's winner. Everything else keeps its registration order; a cycle or a missing
    // dependency falls back to the remaining registration order instead of failing the build over
    // ordering alone.
    private static IEnumerable<ProtoTargetChain> Order(IReadOnlyList<ProtoTargetChain> chains)
    {
        if (chains.All(chain => chain.ResolveAfter.Count == 0))
        {
            return chains;
        }

        var resolved = new HashSet<string>(StringComparer.Ordinal);
        var pending = new List<ProtoTargetChain>(chains);
        var ordered = new List<ProtoTargetChain>(chains.Count);
        while (pending.Count > 0)
        {
            var next = pending.FirstOrDefault(chain => chain.ResolveAfter.All(resolved.Contains))
                ?? pending[0];
            pending.Remove(next);
            resolved.Add(next.Name);
            ordered.Add(next);
        }

        return ordered;
    }

    private static ProtoTargetResolution ResolveTarget(
        IConfiguration configuration,
        ProtoTargetChain chain,
        IReadOnlyDictionary<string, ProtoResolvedTarget> resolved)
    {
        if (chain.Providers.Count == 0)
        {
            throw new ProtoTargetResolutionException(
                chain.Name,
                $"No provider is registered for target '{chain.Name}'. Add one to its chain: " +
                "UseConfigured() for a target configuration serves, or Use(provider) for a piece.");
        }

        var context = new ProtoProviderConditionContext(configuration, chain.Keys, resolved);
        var skipped = new List<ProtoProviderSkip>(chain.Providers.Count - 1);
        for (var index = 0; index < chain.Providers.Count; index++)
        {
            var provider = chain.Providers[index];
            if (provider.Condition is null || provider.Condition.IsSatisfied(context))
            {
                // Everything after the winner is lower priority, whatever its own condition says: the
                // reason names the provider that already serves the target.
                for (var later = index + 1; later < chain.Providers.Count; later++)
                {
                    skipped.Add(new ProtoProviderSkip(
                        chain.Providers[later],
                        $"The earlier provider '{provider.Name}' serves the target first"));
                }

                return new ProtoTargetResolution(chain.Name, chain.Keys, provider, skipped);
            }

            skipped.Add(new ProtoProviderSkip(provider, provider.Condition.Describe(context)));
        }

        throw new ProtoTargetResolutionException(chain.Name, NoProviderMessage(chain, skipped));
    }

    private static string NoProviderMessage(ProtoTargetChain chain, IReadOnlyList<ProtoProviderSkip> skipped)
    {
        var keys = chain.Keys.Count == 0
            ? "no declared keys"
            : $"keys: {string.Join(", ", chain.Keys)}";
        var conditions = string.Join(
            " ",
            skipped.Select(skip => $"'{skip.Provider.Name}': {skip.Reason}."));
        return $"No provider can serve target '{chain.Name}' ({keys}). Providers tried in order: {conditions}";
    }
}
