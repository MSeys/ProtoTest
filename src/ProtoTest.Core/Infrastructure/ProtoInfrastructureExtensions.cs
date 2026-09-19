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

        // Validate and reserve the resource before touching the service collection: a conflicting
        // instance must fail the whole registration without leaving half-applied settings behind.
        builder.AddResource(infrastructure);

        builder.ConfigureServices(services =>
        {
            // Adding the same infrastructure twice is one lifecycle, but every call's keys count: the
            // repeated registration merges its settings into the existing one so both roots get filled.
            for (var index = 0; index < services.Count; index++)
            {
                if (services[index] is not
                    { ServiceType: var serviceType, ImplementationInstance: ProtoInfrastructureRegistration existing }
                    || serviceType != typeof(ProtoInfrastructureRegistration)
                    || !string.Equals(existing.Infrastructure.Id, infrastructure.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                var merged = existing.Settings.Concat(keys).Distinct(StringComparer.Ordinal).ToArray();
                services[index] = ServiceDescriptor.Singleton(
                    new ProtoInfrastructureRegistration(existing.Infrastructure, merged));
                return;
            }

            services.AddSingleton(new ProtoInfrastructureRegistration(infrastructure, [.. keys]));
        });
        return builder;
    }
}
