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

        // A started instance published by a settings piece is the address the suite actually talks to.
        var address = context.Settings.Values.TryGetValue(BaseUrlKey, out var published) ? published : null;
        address = string.IsNullOrWhiteSpace(address) ? context.Configuration[BaseUrlKey] : address;
        if (string.IsNullOrWhiteSpace(address))
        {
            _evidence = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["readiness.skipped"] = "no published address; the application runs in-process"
            };
            return;
        }

        if (!Uri.TryCreate(address, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException(
                $"'{BaseUrlKey}' is '{address}', which is not an absolute URL, so the readiness probe cannot use it.");
        }

        var url = new Uri(baseUri, _path);
        var timeout = _timeoutOverride ?? _options.Timeout;
        var result = await ProtoReadiness.WaitAsync(
            $"{_applicationName} address",
            ProtoReadiness.Http(url, _ready),
            timeout,
            _options.Interval,
            cancellationToken).ConfigureAwait(false);

        _evidence = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["readiness.url"] = url.ToString(),
            ["readiness.attempts"] = result.Attempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["readiness.waitedMs"] = ((long)result.Waited.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    public ValueTask ReleaseAsync(ProtoResourceReleaseContext context) => ValueTask.CompletedTask;
}
