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
    /// <paramref name="configure"/>. It is the shorthand for the chain overload with
    /// <c>UseHost()</c>, so the top-level worker is hosted in this process and bridges the test clock.
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

        var options = new ProtoWorkerOptions();
        configure?.Invoke(options);
        var registrations = ClaimWorker<TProgram>(builder, name);
        if (registrations is null)
        {
            // The same worker is already registered under this name: one lifecycle, no-op.
            return builder;
        }

        var worker = new ProtoWorkerChainBuilder<TProgram>(name, options, registrations.Registry);
        worker.UseHost();
        worker.Register(builder);
        RegisterRegistry(builder, registrations);
        return builder;
    }

    /// <summary>
    /// Registers a worker not attached to an application as a provider chain: the chain decides where
    /// the worker runs - <c>UseHost()</c> hosts its entry point in this process (the default for a
    /// top-level worker), and a provider a future release ships runs the real image instead.
    /// </summary>
    /// <typeparam name="TProgram">The entry point class of the worker application.</typeparam>
    /// <param name="builder">The <see cref="IProtoHostBuilder"/> instance.</param>
    /// <param name="name">The worker's name, in configuration and diagnostic messages.</param>
    /// <param name="configure">Adds the worker's providers in priority order; with none, the worker defaults to <c>UseHost()</c>.</param>
    public static IProtoHostBuilder AddWorkerHost<TProgram>(
        this IProtoHostBuilder builder,
        string name,
        Action<IProtoWorkerBuilder> configure) where TProgram : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        var registrations = ClaimWorker<TProgram>(builder, name);
        if (registrations is null)
        {
            return builder;
        }

        var worker = new ProtoWorkerChainBuilder<TProgram>(name, new ProtoWorkerOptions(), registrations.Registry);
        configure(worker);
        worker.Register(builder);
        RegisterRegistry(builder, registrations);
        return builder;
    }

    /// <summary>
    /// Nests a background worker under an application: it follows the application's provider chain -
    /// hosted in this process when the application runs in-process, run by the environment when a
    /// configured, loopback or AppHost provider serves it (the default chain), and the explicit
    /// providers a future release adds (a container image) in between.
    /// </summary>
    /// <typeparam name="TProgram">The entry point class of the worker application.</typeparam>
    /// <param name="application">The application the worker belongs to.</param>
    /// <param name="name">The worker's name, in configuration and diagnostic messages. Defaults to "Default".</param>
    /// <param name="configure">
    /// Adds the worker's providers in priority order. Omitted, the worker resolves
    /// <c>UseEnvironment().UseHost()</c>: the environment that runs the application runs its worker,
    /// and an application the run hosts in-process hosts its worker too.
    /// </param>
    public static IProtoApplicationBuilder AddWorkerHost<TProgram>(
        this IProtoApplicationBuilder application,
        string name = "Default",
        Action<IProtoWorkerBuilder>? configure = null) where TProgram : class
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var registrations = ClaimWorker<TProgram>(application.Host, name);
        if (registrations is null)
        {
            return application;
        }

        var worker = new ProtoWorkerChainBuilder<TProgram>(
            name, new ProtoWorkerOptions(), registrations.Registry, application.ApplicationName);
        if (configure is null)
        {
            worker.UseEnvironment();
            worker.UseHost();
        }
        else
        {
            configure(worker);
        }

        worker.Register(application.Host);
        RegisterRegistry(application.Host, registrations);
        return application;
    }

    private static Registrations? ClaimWorker<TProgram>(IProtoHostBuilder builder, string name)
    {
        var registrations = RegisteredWorkers.GetValue(builder, static _ => new Registrations());
        if (registrations.Programs.TryGetValue(name, out var registeredProgram))
        {
            if (registeredProgram == typeof(TProgram))
            {
                // A repeated registration of the same worker under one name is the same lifecycle.
                return null;
            }

            // A dropped duplicate that is a different program is a silent wrong state, not a no-op.
            throw new InvalidOperationException(
                $"A worker named '{name}' is already registered for {registeredProgram.FullName}; " +
                $"register {typeof(TProgram).FullName} under a different name instead.");
        }

        registrations.Programs.Add(name, typeof(TProgram));
        return registrations;
    }

    private static void RegisterRegistry(IProtoHostBuilder builder, Registrations registrations)
    {
        if (registrations.RegistryRegistered)
        {
            return;
        }

        builder.ConfigureServices(services => services.AddSingleton(registrations.Registry));
        registrations.RegistryRegistered = true;
    }

    private sealed class Registrations
    {
        public Dictionary<string, Type> Programs { get; } = new(StringComparer.Ordinal);

        public ProtoWorkerRegistry Registry { get; } = new();

        public bool RegistryRegistered { get; set; }
    }
}
