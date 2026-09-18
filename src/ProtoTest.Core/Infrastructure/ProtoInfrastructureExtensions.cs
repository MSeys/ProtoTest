namespace ProtoTest.Core;

using Microsoft.Extensions.DependencyInjection;

public static class ProtoInfrastructureExtensions
{
    /// <summary>
    /// Registers infrastructure the host starts before the run. Every key in
    /// <paramref name="settings"/> receives the started connection string, so the tests and an
    /// in-process application can read it under their own configuration roots.
    /// </summary>
    public static IProtoHostBuilder AddInfrastructure(
        this IProtoHostBuilder builder,
        IProtoInfrastructure infrastructure,
        params string[] settings)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(infrastructure);
        var keys = settings ?? [];
        if (infrastructure.Scope != ProtoResourceScope.Run)
        {
            throw new ArgumentException(
                $"{infrastructure.GetType().Name} must be run-scoped (ProtoResourceScope.Run); infrastructure outlives a test.",
                nameof(infrastructure));
        }

        if (keys.Length > 0 && infrastructure is not IProtoConnectionInfrastructure)
        {
            throw new ArgumentException(
                $"{infrastructure.GetType().Name} does not provide a connection string, so it cannot fill configuration keys.",
                nameof(settings));
        }

        builder.ConfigureServices(services =>
            services.AddSingleton(new ProtoInfrastructureRegistration(infrastructure, [.. keys])));
        return builder.AddResource(infrastructure);
    }
}
