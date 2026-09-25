namespace ProtoTest.Core;

using Microsoft.Extensions.DependencyInjection;

public static class ProtoInfrastructureExtensions
{
    /// <summary>
    /// Registers infrastructure the host starts before the run. Every key in
    /// <paramref name="settings"/> receives the started connection string, so the tests and an
    /// in-process application can read it under their own configuration roots; for a
    /// settings-only piece the keys are the values it will fill, and they decide whether it is needed.
    /// </summary>
    /// <remarks>
    /// The piece is <b>not started</b> when every declared key already has a configured value: the
    /// environment provides those addresses, and starting a container or process would shadow them.
    /// Use <see cref="AddInfrastructureAlways"/> when a piece must start regardless.
    /// </remarks>
    public static IProtoHostBuilder AddInfrastructure(
        this IProtoHostBuilder builder,
        IProtoInfrastructure infrastructure,
        params string[] settings)
        => Register(builder, infrastructure, alwaysStart: false, settings);

    /// <summary>
    /// Registers infrastructure that starts even when its declared keys are already configured.
    /// </summary>
    public static IProtoHostBuilder AddInfrastructureAlways(
        this IProtoHostBuilder builder,
        IProtoInfrastructure infrastructure,
        params string[] settings)
        => Register(builder, infrastructure, alwaysStart: true, settings);

    private static IProtoHostBuilder Register(
        IProtoHostBuilder builder,
        IProtoInfrastructure infrastructure,
        bool alwaysStart,
        string[]? settings)
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

        if (keys.Length > 0 && infrastructure is not (IProtoConnectionInfrastructure or IProtoSettingsInfrastructure))
        {
            throw new ArgumentException(
                $"{infrastructure.GetType().Name} does not provide an address, so it cannot declare configuration keys.",
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
                    new ProtoInfrastructureRegistration(
                        existing.Infrastructure,
                        merged,
                        existing.AlwaysStart || alwaysStart));
                return;
            }

            services.AddSingleton(new ProtoInfrastructureRegistration(infrastructure, [.. keys], alwaysStart));
        });
        return builder;
    }
}
