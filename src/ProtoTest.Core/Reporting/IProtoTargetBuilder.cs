namespace ProtoTest.Core;

using Microsoft.Extensions.DependencyInjection;

/// <summary>Configures observation collection for a named target.</summary>
public interface IProtoTargetBuilder
{
    /// <summary>Gets the target name used to match observations.</summary>
    string TargetName { get; }

    /// <summary>Gets the service collection used to register target services.</summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Registers an observation collector. The collector must expose a constructor accepting the
    /// target name, any <paramref name="additionalArguments"/>, and optionally services resolvable
    /// from DI.
    /// </summary>
    public IProtoTargetBuilder WithCollector<TCollector>(params object[] additionalArguments)
        where TCollector : class, IProtoCollector
    {
        Services.AddSingleton<IProtoCollector>(sp =>
            ActivatorUtilities.CreateInstance<TCollector>(sp, [TargetName, .. additionalArguments]));
        return this;
    }
}
