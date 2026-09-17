namespace ProtoTest.Web;

using Microsoft.Extensions.Configuration;

/// <summary>
/// Applies configuration to a backend's options the first time a session needs them.
/// Code configuration is applied first; <c>ProtoTest:Web:{backend}</c> then overrides it for every session
/// of that backend, and <c>ProtoTest:Web:Sessions:{session}</c> overrides it for one named session.
/// </summary>
/// <remarks>
/// Backend registrations run before the host's configuration is built, so binding is deferred to the first
/// backend creation and performed exactly once.
/// </remarks>
public sealed class WebBackendOptionsBinder<TOptions> where TOptions : class
{
    private readonly TOptions _options;
    private readonly string _backendName;
    private readonly string _sessionName;
    private readonly Action<TOptions>? _validate;
    private readonly ProtoLock _gate = new();
    private bool _bound;

    public WebBackendOptionsBinder(TOptions options, string backendName, string sessionName, Action<TOptions>? validate = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(backendName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);
        _options = options;
        _backendName = backendName;
        _sessionName = sessionName;
        _validate = validate;
        _validate?.Invoke(options);
    }

    public static string BackendSection(string backendName) => $"ProtoTest:Web:{backendName}";

    public static string SessionSection(string sessionName) => $"ProtoTest:Web:Sessions:{sessionName}";

    public TOptions Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (Volatile.Read(ref _bound)) return _options;

        lock (_gate)
        {
            if (_bound) return _options;
            configuration.GetSection(BackendSection(_backendName)).Bind(_options);
            configuration.GetSection(SessionSection(_sessionName)).Bind(_options);
            _validate?.Invoke(_options);
            _bound = true;
        }

        return _options;
    }
}
