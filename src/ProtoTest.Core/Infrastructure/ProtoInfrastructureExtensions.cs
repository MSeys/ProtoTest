namespace ProtoTest.Core;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core.Internal;

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

    /// <summary>
    /// Registers a run-scoped setup step: an action the host runs once when the run starts, at this
    /// registration's position in the infrastructure order. Register it after the pieces whose
    /// published settings it reads - a container's connection string, a broker's address - so it sees
    /// their started values; the run's cancellation token arrives through
    /// <see cref="ProtoRunSetupContext.CancellationToken"/>.
    /// </summary>
    /// <remarks>
    /// A step is an action, not a resource: it owns nothing to release, so the run's stop and dispose
    /// do not run it again. A step that throws fails the run's start with its own exception, releases
    /// what the run had started and leaves the host retryable, exactly like failing infrastructure; a
    /// retry runs the step again. Use it for state the whole run shares - a schema for a container
    /// database - because a run hook runs before infrastructure starts and cannot see its addresses,
    /// and a test hook or test body runs inside the per-test transaction, where its DDL is rolled back
    /// with the test.
    /// </remarks>
    public static IProtoHostBuilder AddRunSetup(
        this IProtoHostBuilder builder,
        string name,
        Func<ProtoRunSetupContext, ValueTask> setup)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(setup);
        return builder.AddInfrastructure(new ProtoRunSetupInfrastructure(name, setup));
    }

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
