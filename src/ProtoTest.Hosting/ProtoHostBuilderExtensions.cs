namespace ProtoTest.Hosting;

using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Hosting.Internal;

/// <summary>Registers background workers the run starts, owns and stops.</summary>
public static class ProtoHostBuilderExtensions
{
    // First registration wins per worker name, mirroring AddAspNetCoreServer: a helper invoked twice
    // cannot start the same worker twice, while a different name still composes.
    private static readonly ConditionalWeakTable<IProtoHostBuilder, Registrations> RegisteredWorkers = new();

    /// <summary>
    /// Hosts a background worker in-process for the whole run: the suite runs the worker's own entry
    /// point to build its host, starts it after the infrastructure registered before it, and stops it
    /// when the run ends. The worker reads the run's settings (started containers' connection strings
    /// and the suite's configuration) through its normal configuration; override values with
    /// <paramref name="configure"/>.
    /// </summary>
    /// <typeparam name="TProgram">The entry point class of the worker application.</typeparam>
    /// <param name="builder">The <see cref="IProtoHostBuilder"/> instance.</param>
    /// <param name="name">The worker's name, in configuration and diagnostic messages. Defaults to "Default".</param>
    /// <param name="configure">Optional configuration the suite applies on top of the run's settings.</param>
    public static IProtoHostBuilder AddWorkerHost<TProgram>(
        this IProtoHostBuilder builder,
        string name = "Default",
        Action<ProtoWorkerOptions>? configure = null) where TProgram : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var registrations = RegisteredWorkers.GetValue(builder, static _ => new Registrations());
        if (registrations.Programs.TryGetValue(name, out var registeredProgram))
        {
            if (registeredProgram == typeof(TProgram))
            {
                return builder;
            }

            // A dropped duplicate that is a different program is a silent wrong state, not a no-op.
            throw new InvalidOperationException(
                $"A worker named '{name}' is already registered for {registeredProgram.FullName}; " +
                $"register {typeof(TProgram).FullName} under a different name instead.");
        }

        var options = new ProtoWorkerOptions();
        configure?.Invoke(options);

        var worker = new ProtoWorkerHost<TProgram>(name, options, registrations.Registry);
        var factory = HostFactoryResolver.ResolveHostFactory(
            typeof(TProgram).Assembly,
            configureHostBuilder: worker.ConfigureBuilder)
            ?? throw new InvalidOperationException(
                $"No host factory could be resolved for {typeof(TProgram).FullName}. The worker assembly needs an " +
                "entry point that builds an IHost (Host.CreateApplicationBuilder or Host.CreateDefaultBuilder).");
        worker.UseFactory(factory);

        builder.AddInfrastructure(worker);
        if (!registrations.RegistryRegistered)
        {
            builder.ConfigureServices(services => services.AddSingleton(registrations.Registry));
            registrations.RegistryRegistered = true;
        }

        registrations.Programs.Add(name, typeof(TProgram));
        return builder.AddCapability(new ProtoCapabilityDescriptor(
            typeof(TProgram).Assembly.GetName().Name ?? typeof(TProgram).FullName!,
            ProtoCapabilityKinds.Worker,
            "ProtoTest.Hosting"));
    }

    private sealed class Registrations
    {
        public Dictionary<string, Type> Programs { get; } = new(StringComparer.Ordinal);

        public ProtoWorkerRegistry Registry { get; } = new();

        public bool RegistryRegistered { get; set; }
    }
}
