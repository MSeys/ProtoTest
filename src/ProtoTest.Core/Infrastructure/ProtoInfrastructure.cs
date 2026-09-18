namespace ProtoTest.Core;

/// <summary>
/// Something the run provides for itself - a database, a broker, a storage emulator. The host starts
/// every registered piece before the run hooks, records it as a run entity, and releases it with the
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
/// Infrastructure that fills configuration keys with values only it can know after starting, for example
/// the address of a standalone application a browser should visit.
/// </summary>
public interface IProtoSettingsInfrastructure : IProtoInfrastructure
{
    /// <summary>Gets the configuration values the started piece provides.</summary>
    IReadOnlyDictionary<string, string> Settings { get; }
}

/// <summary>
/// Registered infrastructure with the configuration keys it provides, for example
/// <c>ConnectionStrings:Northstar</c> for the application and <c>ProtoTest:...</c> for the adapter. The
/// host exposes started values through <see cref="ProtoInfrastructureSettings"/>, and the in-process
/// application receives them as host settings automatically.
/// </summary>
public sealed record ProtoInfrastructureRegistration(
    IProtoInfrastructure Infrastructure,
    IReadOnlyList<string> Settings);

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
}
