namespace ProtoTest.Http;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

/// <summary>
/// Records a collector registered for a target, so a repeated <see cref="ProtoTargetBuilder.AddCollector{TCollector}"/>
/// call for the same type and target registers once instead of reporting twice.
/// </summary>
internal sealed record ProtoCollectorRegistration(string TargetName, Type CollectorType);

/// <summary>Shared, protocol-neutral <see cref="IProtoTargetBuilder"/> for every protocol integration.</summary>
public sealed class ProtoTargetBuilder(string targetName, IServiceCollection services) : IProtoTargetBuilder
{
    public string TargetName { get; } = targetName;
    public IServiceCollection Services { get; } = services;

    /// <summary>
    /// Registers an observation collector once per collector type and target. The interface's default
    /// implementation uses a plain singleton, so a repeated call would collect and report twice.
    /// </summary>
    public IProtoTargetBuilder AddCollector<TCollector>(params object[] additionalArguments)
        where TCollector : class, IProtoCollector
    {
        ArgumentNullException.ThrowIfNull(additionalArguments);
        var marker = new ProtoCollectorRegistration(TargetName, typeof(TCollector));
        if (!ProtoRegistration.TryAdd(Services, marker, existing => existing == marker))
        {
            return this;
        }

        Services.AddSingleton<IProtoCollector>(provider =>
            ActivatorUtilities.CreateInstance<TCollector>(provider, [TargetName, .. additionalArguments]));
        return this;
    }
}
