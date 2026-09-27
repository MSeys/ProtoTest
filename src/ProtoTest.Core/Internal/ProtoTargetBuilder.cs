namespace ProtoTest.Core.Internal;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Collects one target's providers while its registration callback runs. The pieces register with the
/// run only after the callback returned, so a failing chain registration leaves no half-registered
/// target.
/// </summary>
internal sealed class ProtoProviderChainBuilder(
    string targetName,
    IReadOnlyList<string> keys,
    IServiceCollection services,
    Action? throwIfBuilt = null) : IProtoProviderChainBuilder
{
    private readonly List<IProtoTargetProvider> _providers = [];
    private readonly List<string> _resolveAfter = [];

    public string TargetName { get; } = targetName;

    public IReadOnlyList<string> Keys { get; } = keys;

    public IServiceCollection Services { get; } = services;

    /// <summary>Gets the providers in the order they were added; the first satisfied one wins.</summary>
    public IReadOnlyList<IProtoTargetProvider> Providers => _providers;

    /// <summary>Gets the targets this chain resolves after, in declaration order.</summary>
    public IReadOnlyList<string> ResolveAfterTargets => _resolveAfter;

    public IProtoProviderChainBuilder ResolveAfter(params string[] targetNames)
    {
        throwIfBuilt?.Invoke();
        foreach (var targetName in targetNames ?? [])
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
            if (!_resolveAfter.Contains(targetName, StringComparer.Ordinal))
            {
                _resolveAfter.Add(targetName);
            }
        }

        return this;
    }

    public IProtoProviderChainBuilder Use(IProtoTargetProvider provider)
    {
        throwIfBuilt?.Invoke();
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider.Name);
        if (_providers.Any(existing => string.Equals(existing.Name, provider.Name, StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                $"A provider named '{provider.Name}' is already registered on target '{TargetName}'; " +
                "every provider of one chain needs its own name.",
                nameof(provider));
        }

        _providers.Add(provider);
        return this;
    }

    public IProtoProviderChainBuilder UseConfigured()
        => Use(new ProtoTargetProvider("configured", Condition: ProtoProviderConditions.Configured));
}
