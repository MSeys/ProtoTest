namespace ProtoTest.AspNetCore;

using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// One per-test service substitution for an in-process server. The substitution is applied to a
/// dedicated server the test owns before that server is built, so the run's shared server never
/// sees it and the next test starts clean. Every shape registers as a singleton: the dedicated
/// server lives for one test, so a singleton replacement is effectively test-scoped.
/// </summary>
internal abstract record ServiceSubstitution(Type ServiceType, string ServerName)
{
    /// <summary>The trace operation kind the substitution records.</summary>
    public abstract string OperationKind { get; }

    /// <summary>What replaced the service, for trace attributes.</summary>
    public abstract string Replacement { get; }

    /// <summary>
    /// Distinguishes two substitutions over the same service, so applying the same union twice does
    /// not rebuild the server while a changed union does.
    /// </summary>
    public abstract string Signature { get; }

    /// <summary>Applies the substitution to the dedicated server's services.</summary>
    public abstract void ApplyTo(IServiceCollection services);
}

/// <summary>Serves a fixed instance for the substituted service.</summary>
internal sealed record InstanceSubstitution(Type ServiceType, string ServerName, object Instance)
    : ServiceSubstitution(ServiceType, ServerName)
{
    public override string OperationKind => "service.substitute";

    public override string Replacement => $"instance:{Instance.GetType().FullName}";

    public override string Signature => $"instance:{RuntimeHelpers.GetHashCode(Instance)}";

    public override void ApplyTo(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Replace(ServiceDescriptor.Singleton(ServiceType, Instance));
    }
}

/// <summary>Resolves the substituted service from an implementation type through the container.</summary>
internal sealed record TypeSubstitution(Type ServiceType, string ServerName, Type ImplementationType)
    : ServiceSubstitution(ServiceType, ServerName)
{
    public override string OperationKind => "service.substitute";

    public override string Replacement => ImplementationType.FullName!;

    public override string Signature => $"type:{ImplementationType.FullName}";

    public override void ApplyTo(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Replace(ServiceDescriptor.Singleton(ServiceType, ImplementationType));
    }
}

/// <summary>Builds the substituted service with a factory at resolution time.</summary>
internal sealed record FactorySubstitution(Type ServiceType, string ServerName, Func<object> Factory)
    : ServiceSubstitution(ServiceType, ServerName)
{
    public override string OperationKind => "service.substitute";

    public override string Replacement => "factory";

    public override string Signature => $"factory:{RuntimeHelpers.GetHashCode(Factory)}";

    public override void ApplyTo(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Replace(new ServiceDescriptor(ServiceType, _ => Factory(), ServiceLifetime.Singleton));
    }
}

/// <summary>Fails the substituted service: resolving it throws instead of serving an instance.</summary>
internal sealed record FailSubstitution(Type ServiceType, string ServerName)
    : ServiceSubstitution(ServiceType, ServerName)
{
    public override string OperationKind => "service.fail";

    public override string Replacement => "failed";

    public override string Signature => "fail";

    public override void ApplyTo(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Replace(new ServiceDescriptor(
            ServiceType,
            _ => throw new InvalidOperationException(
                $"The '{ServiceType.FullName}' dependency fails for this test ([FailDependency])."),
            ServiceLifetime.Singleton));
    }
}
