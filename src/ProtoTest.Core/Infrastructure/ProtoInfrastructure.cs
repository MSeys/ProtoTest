namespace ProtoTest.Core;

using Microsoft.Extensions.Configuration;

/// <summary>
/// Something the run provides for itself - a database, a broker, a storage emulator. The host starts
/// every registered piece after the run hooks, records it as a run entity, and releases it with the
/// run, so a test suite declares what it needs instead of starting things by hand.
/// </summary>
public interface IProtoInfrastructure : IProtoResource
{
    /// <summary>Starts the piece; a failure must explain what could not start and why.</summary>
    ValueTask StartAsync(CancellationToken cancellationToken = default);
}

/// <summary>Infrastructure that hands the application under test a connection string.</summary>
public interface IProtoConnectionInfrastructure : IProtoInfrastructure
{
    /// <summary>Gets the connection string of the started piece.</summary>
    string ConnectionString { get; }
}

/// <summary>
/// The run's collected state, handed to infrastructure as it starts: the settings earlier
/// infrastructure provided, the suite's own configuration, and the clock in effect (the active test's,
/// or the run's on a background flow).
/// </summary>
public sealed record ProtoInfrastructureContext(
    ProtoInfrastructureSettings Settings,
    IConfiguration Configuration,
    TimeProvider TimeProvider);

/// <summary>
/// Infrastructure that needs the run's collected state while it starts - settings earlier pieces
/// provided and the suite's configuration, which are complete only once those pieces have started. The
/// host prefers this overload over <see cref="IProtoInfrastructure.StartAsync(CancellationToken)"/>
/// when a piece implements it, so an in-process worker can read the database or broker a container just
/// started, and a readiness probe can read the address a settings piece published.
/// </summary>
public interface IProtoConfiguredInfrastructure : IProtoInfrastructure
{
    /// <summary>
    /// The host prefers the configured overload and always calls it; this default exists so an
    /// implementer only writes the overload it needs. Starting without the run's state is only
    /// reachable from a host that does not offer it.
    /// </summary>
    ValueTask IProtoInfrastructure.StartAsync(CancellationToken cancellationToken)
        => StartAsync(
            new ProtoInfrastructureContext(
                new ProtoInfrastructureSettings(),
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
                TimeProvider.System),
            cancellationToken);

    /// <summary>Starts the piece with the run's collected state.</summary>
    ValueTask StartAsync(ProtoInfrastructureContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Infrastructure that can report what happened while it started - attempts, the wait it spent, the
/// last error it saw. The host merges the evidence into the run entity it records for the piece, so a
/// readiness wait is visible in the trace without an integration of its own.
/// </summary>
public interface IProtoStartupEvidence
{
    /// <summary>Gets the evidence to merge into the piece's run entity after it started.</summary>
    IReadOnlyDictionary<string, string?> StartupEvidence { get; }
}

/// <summary>
/// Infrastructure that fills configuration keys with values only it can know after starting, for example
/// the address of a standalone application a browser should visit.
/// </summary>
public interface IProtoSettingsInfrastructure : IProtoInfrastructure
{
    /// <summary>Gets the configuration values the started piece provides.</summary>
    IReadOnlyDictionary<string, string> Settings { get; }
}

/// <summary>
/// Registered infrastructure with the configuration keys it provides - a connection string per key, or
/// the keys a settings-only piece will fill. The host exposes started values through
/// <see cref="ProtoInfrastructureSettings"/>, and the in-process application receives them as host
/// settings automatically.
/// </summary>
internal sealed record ProtoInfrastructureRegistration(
    IProtoInfrastructure Infrastructure,
    IReadOnlyList<string> Settings,
    bool AlwaysStart = false)
{
    /// <summary>
    /// Whether every key this piece declares already has a configured value, so the environment
    /// provides its addresses and the piece must not start and shadow them.
    /// </summary>
    public bool IsSatisfiedBy(IConfiguration configuration)
        => !AlwaysStart
           && Settings.Count > 0
           && Settings.All(key => !string.IsNullOrWhiteSpace(configuration[key]));
}

/// <summary>The infrastructure pieces the environment already satisfies, so the host skips them.</summary>
internal sealed record ProtoSkippedInfrastructure(IReadOnlySet<string> Ids);

/// <summary>The configuration values started infrastructure provided, keyed as the application reads them.</summary>
public sealed class ProtoInfrastructureSettings
{
    private readonly ProtoLock _gate = new();
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    /// <summary>Gets a snapshot of the settings.</summary>
    public IReadOnlyDictionary<string, string> Values
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<string, string>(_values, StringComparer.Ordinal);
            }
        }
    }

    internal void Set(string key, string value)
    {
        lock (_gate)
        {
            _values[key] = value;
        }
    }

    /// <summary>
    /// Forgets every value. Called when the infrastructure that filled them is released, so a retry or
    /// an in-process application cannot keep reading a released instance's connection string.
    /// </summary>
    internal void Clear()
    {
        lock (_gate)
        {
            _values.Clear();
        }
    }
}

