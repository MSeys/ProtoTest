namespace ProtoTest.Core;

using ProtoTest.Core.Internal;

/// <summary>Registers readiness probes the host awaits before the first test.</summary>
public static class ProtoReadinessExtensions
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IProtoHostBuilder, ProtoReadinessOptions> Options = new();

    /// <summary>
    /// The one <see cref="ProtoReadinessOptions"/> instance for a builder: every probe and every
    /// container the run starts resolves through it, so one policy governs every wait.
    /// </summary>
    internal static ProtoReadinessOptions ResolveOptions(IProtoHostBuilder builder)
        => Options.GetValue(builder, static _ => new ProtoReadinessOptions());

    /// <summary>
    /// Changes how long the host waits for probes and how often it asks. Can be called before or after
    /// the probes are registered; later values still apply. One instance governs the host's probes and
    /// every container the run starts; the section <c>ProtoTest:Readiness</c> binds over the code
    /// values when the host is built.
    /// </summary>
    public static IProtoHostBuilder ConfigureReadiness(
        this IProtoHostBuilder builder,
        Action<ProtoReadinessOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        configure(ResolveOptions(builder));
        return builder;
    }

    /// <summary>
    /// Registers a readiness check the host awaits at run start, in registration order - register it
    /// after the piece it waits for. Use <see cref="ProtoReadiness.Tcp"/> and
    /// <see cref="ProtoReadiness.Http"/> for the common cases, or a delegate for anything else.
    /// </summary>
    /// <param name="builder">The <see cref="IProtoHostBuilder"/> instance.</param>
    /// <param name="name">The probe's name in the trace and in a timeout failure.</param>
    /// <param name="probe">The check; polled until it returns true or the timeout expires.</param>
    /// <param name="timeout">Overrides <see cref="ProtoReadinessOptions.Timeout"/> for this probe.</param>
    public static IProtoHostBuilder AddReadinessProbe(
        this IProtoHostBuilder builder,
        string name,
        Func<CancellationToken, ValueTask<bool>> probe,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(probe);

        var options = ResolveOptions(builder);
        return builder.AddInfrastructure(new ReadinessProbeInfrastructure(name, probe, options, timeout));
    }

    /// <summary>
    /// Awaits the address an application is published at: reads
    /// <c>ProtoTest:Applications:{applicationName}:BaseUrl</c> (or the value a started piece published
    /// under the same key) and probes it at run start. An application running in-process has no address
    /// and needs no wait, so the probe is skipped and says so in the trace.
    /// </summary>
    /// <remarks>
    /// Register the probe <b>after</b> the piece that publishes the address: probes are awaited at
    /// their registration position, so a probe registered first resolves nothing. It does not claim the
    /// application runs in-process; it records <c>readiness.skipped</c> naming its position and the
    /// later publisher. An application whose address is only ever configured can be probed anywhere.
    /// </remarks>
    /// <param name="builder">The <see cref="IProtoHostBuilder"/> instance.</param>
    /// <param name="applicationName">The application name, as registered with <c>AddApplication</c>.</param>
    /// <param name="path">The path to request; defaults to <c>/</c>.</param>
    /// <param name="ready">Optional acceptance check; by default any HTTP response counts as ready.</param>
    /// <param name="timeout">Overrides <see cref="ProtoReadinessOptions.Timeout"/> for this probe.</param>
    public static IProtoHostBuilder AddHttpReadiness(
        this IProtoHostBuilder builder,
        string applicationName,
        string path = "/",
        Func<HttpResponseMessage, bool>? ready = null,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);

        var options = ResolveOptions(builder);
        return builder.AddInfrastructure(new ApplicationReadinessInfrastructure(applicationName, path, ready, options, timeout));
    }
}
