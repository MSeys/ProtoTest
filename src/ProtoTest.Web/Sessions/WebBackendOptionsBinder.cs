namespace ProtoTest.Web;

using Microsoft.Extensions.Configuration;
using ProtoTest.Core;

/// <summary>
/// Applies configuration to a backend's options the first time a session needs them. Code configuration
/// is applied first; the options' own section (<c>ProtoTest:Web:{backend}</c>, from
/// <see cref="IProtoConfigurableOptions"/>) then overrides it for every session of that backend, and
/// <c>ProtoTest:Web:Sessions:{session}</c> overrides it for one named session.
/// </summary>
/// <remarks>
/// Backend registrations run before the host's configuration is built, so binding is deferred to the
/// first backend creation and performed exactly once.
/// </remarks>
public sealed class WebBackendOptionsBinder<TOptions>
    where TOptions : class, IProtoConfigurableOptions
{
    private readonly TOptions _options;
    private readonly string _sessionName;
    private readonly Action<TOptions>? _validate;
    private readonly ProtoLock _gate = new();
    private bool _bound;

    public WebBackendOptionsBinder(TOptions options, string sessionName, Action<TOptions>? validate = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);
        _options = options;
        _sessionName = sessionName;
        _validate = validate;
        _validate?.Invoke(options);
    }

    /// <summary>Returns the configuration section that overrides options for one named session.</summary>
    public static string SessionSection(string sessionName) => $"ProtoTest:Web:Sessions:{sessionName}";

    /// <summary>Binds the backend and session sections once, returning the resolved options.</summary>
    public TOptions Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (Volatile.Read(ref _bound)) return _options;

        lock (_gate)
        {
            if (_bound) return _options;
            _options.BindFromConfiguration(configuration);
            configuration.GetSection(SessionSection(_sessionName)).Bind(_options);
            _validate?.Invoke(_options);
            _bound = true;
        }

        return _options;
    }
}
