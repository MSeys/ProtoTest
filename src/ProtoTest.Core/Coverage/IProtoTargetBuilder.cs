namespace ProtoTest.Core;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Configures coverage collection for a named target.
/// </summary>
public interface IProtoTargetBuilder
{
    /// <summary>
    /// Gets the target name used to match coverage hits.
    /// </summary>
    string TargetName { get; }

    /// <summary>
    /// Gets the service collection used to register target services.
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Registers a collector. The collector must expose a constructor accepting
    /// the target name, optionally followed by services resolvable from DI.
    /// </summary>
    public IProtoTargetBuilder WithCoverage<TCollector>()
        where TCollector : class, IProtoCollector
    {
        Services.AddSingleton<IProtoCollector>(sp =>
        {
            return ActivatorUtilities.CreateInstance<TCollector>(sp, TargetName);
        });

        return this;
    }
}