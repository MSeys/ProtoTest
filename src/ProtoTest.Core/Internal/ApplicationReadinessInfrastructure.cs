namespace ProtoTest.Core.Internal;

using System.Net.Http;

/// <summary>
/// Waits for the address an application is published at, read from the run's settings first (a started
/// standalone instance wins, matching the web session rule) and then from
/// <c>ProtoTest:Applications:{name}:BaseUrl</c>. An application running in-process has no address: the
/// wait is skipped and the trace says so.
/// </summary>
internal sealed class ApplicationReadinessInfrastructure : IProtoConfiguredInfrastructure, IProtoStartupEvidence
{
    private readonly string _applicationName;
    private readonly string _path;
    private readonly Func<HttpResponseMessage, bool>? _ready;
    private readonly ProtoReadinessOptions _options;
    private readonly TimeSpan? _timeoutOverride;
    private Dictionary<string, string?> _evidence = new(StringComparer.Ordinal);

    public ApplicationReadinessInfrastructure(
        string applicationName,
        string path,
        Func<HttpResponseMessage, bool>? ready,
        ProtoReadinessOptions options,
        TimeSpan? timeoutOverride)
    {
        _applicationName = applicationName;
        _path = string.IsNullOrWhiteSpace(path) ? "/" : path;
        _ready = ready;
        _options = options;
        _timeoutOverride = timeoutOverride;
    }

    private string BaseUrlKey => $"ProtoTest:Applications:{_applicationName}:BaseUrl";

    public string Id => $"readiness:application:{_applicationName}";

    public string Kind => "readiness";

    public string Description => $"Readiness · {_applicationName} address";

    public ProtoResourceScope Scope => ProtoResourceScope.Run;

    public IReadOnlyDictionary<string, string?> StartupEvidence => _evidence;

    public async ValueTask StartAsync(ProtoInfrastructureContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        _options.Validate();

        // The application-setting precedence every reader shares: a started instance published by a
        // settings piece wins over static configuration.
        var address = ProtoApplication.ResolveSetting(context.Configuration, context.Settings, BaseUrlKey);
        if (string.IsNullOrWhiteSpace(address))
        {
            _evidence = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["readiness.skipped"] = SkipReason(context)
            };
            return;
        }

        if (!Uri.TryCreate(address, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"'{BaseUrlKey}' is '{address}', which is not an absolute HTTP or HTTPS URL, so the readiness probe cannot use it.");
        }

        var url = new Uri(baseUri, _path);
        var timeout = _timeoutOverride ?? _options.Timeout;
        string? lastFailure = null;
        var result = await ProtoReadiness.WaitAsync(
            $"{_applicationName} address at {url}",
            ProtoReadiness.Http(url, _ready, onFailure: reason => lastFailure = reason),
            timeout,
            _options.Interval,
            cancellationToken,
            describeLastError: () => lastFailure is null
                ? "the endpoint answered, but the readiness check rejected every response"
                : lastFailure).ConfigureAwait(false);

        _evidence = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["readiness.url"] = url.ToString(),
            ["readiness.attempts"] = result.Attempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["readiness.waitedMs"] = ((long)result.Waited.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    /// <summary>
    /// Explains a skip without claiming a mode the run cannot prove. A probe registered before the
    /// piece that publishes the address is the common mistake: probes are awaited at their position,
    /// so nothing has published yet - that is an ordering problem, not an in-process application.
    /// </summary>
    private string SkipReason(ProtoInfrastructureContext context)
    {
        if (context.PendingSettings.Contains(BaseUrlKey))
        {
            return $"no published address at this registration position; '{BaseUrlKey}' is declared by " +
                   "infrastructure registered after this probe - register AddHttpReadiness after the " +
                   "piece that publishes it";
        }

        if (context.InProcessServerApplications.Contains(_applicationName))
        {
            return "no published address; the application runs in-process";
        }

        return $"no published address and no in-process server for application '{_applicationName}'; " +
               $"set '{BaseUrlKey}' or back the application with AddAspNetCoreServer";
    }

    public ValueTask ReleaseAsync(ProtoResourceReleaseContext context) => ValueTask.CompletedTask;
}
