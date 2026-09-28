namespace ProtoTest.Extensibility.Tests;

using System.Text;
using ProtoTest.Core;
using ProtoTest.Web;

/// <summary>
/// The backend options a minimal backend resolves: code defaults, then <c>ProtoTest:Web:Minimal</c>,
/// then validation, exactly the precedence the shipped backends use.
/// </summary>
public sealed class MinimalWebOptions : IProtoConfigurableOptions
{
    public const string ConfigurationSectionName = "ProtoTest:Web:Minimal";

    string IProtoConfigurableOptions.ConfigurationSectionName => ConfigurationSectionName;

    public TimeSpan ActionTimeout { get; set; } = WebBackendDefaults.DefaultTimeout;

    public TimeSpan PollInterval { get; set; } = WebBackendDefaults.DefaultPollInterval;

    public void Validate()
    {
        if (ActionTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ActionTimeout));
        }

        if (PollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(PollInterval));
        }
    }
}

/// <summary>
/// A minimal web backend factory written against the public web surface only - this project has no
/// InternalsVisibleTo grant, so it composes exactly what the docs promise a hand-written backend: the
/// options bootstrap, the failure-artifact capture, the error factories, the timing defaults, the
/// artifact naming rule and the shared probe loop.
/// </summary>
public sealed class MinimalWebBackendFactory(Action<MinimalWebOptions>? configure = null) : IWebBackendFactory
{
    /// <summary>The backend name recorded on the browser capability and the trace.</summary>
    public const string BackendName = "Minimal";

    public string Name => BackendName;

    public ValueTask<IWebBackend> CreateAsync(
        ProtoExecutionContext context,
        string sessionName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);
        return ValueTask.FromResult<IWebBackend>(new MinimalWebBackend(context, sessionName, configure));
    }
}

/// <summary>
/// The minimal backend itself: an in-memory model of a browser. A click retries through the shared
/// <see cref="WebProbeLoop"/> until it succeeds on the second attempt, mirroring how Selenium retries
/// actionability; a failure is captured through <see cref="WebFailureArtifacts"/> so the attachments
/// carry the documented names; and the options resolve through <see cref="WebBackendOptions"/>.
/// </summary>
public sealed class MinimalWebBackend : IWebBackend, IWebBackendDiagnostics
{
    private const string TraceSource = "ProtoTest.Web.Minimal";

    private readonly ProtoExecutionContext _context;
    private readonly string _sessionName;
    private readonly MinimalWebOptions _options;
    private readonly WebProbeLoop _probes;
    private string? _address;
    private int _clickAttempts;

    public MinimalWebBackend(
        ProtoExecutionContext context,
        string sessionName,
        Action<MinimalWebOptions>? configure = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _sessionName = string.IsNullOrWhiteSpace(sessionName) ? "Default" : sessionName;
        _options = WebBackendOptions.Resolve(context, configure, options => options.Validate());
        _probes = new WebProbeLoop(() => _options.PollInterval);
    }

    public string Name => MinimalWebBackendFactory.BackendName;

    /// <summary>The number of click attempts, so a test can observe the retry loop.</summary>
    public int ClickAttempts => _clickAttempts;

    /// <summary>The timeout resolved from configuration, so a test can observe the options facade.</summary>
    public TimeSpan ActionTimeout => _options.ActionTimeout;

    public TimeSpan PollInterval => _options.PollInterval;

    /// <summary>The last address a navigation reached; the backend's address contract.</summary>
    public string? Address => _address;

    public ValueTask<string?> GetCurrentAddressAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(_address);

    public ValueTask NavigateAsync(Uri address, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        _address = address.ToString();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Clicks after the element became actionable: the probe fails once and succeeds on the second
    /// attempt, and a loop that never settles reports the documented actionability failure.
    /// </summary>
    public async ValueTask ClickAsync(WebElementReference element, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(element);
        var result = await _probes.PollObservationAsync(
            _ =>
            {
                _clickAttempts++;
                return ValueTask.FromResult(_clickAttempts >= 2
                    ? new WebProbe(true, "action completed")
                    : new WebProbe(false, "element is not actionable yet"));
            },
            probe => probe.Holds,
            _options.ActionTimeout,
            cancellationToken).ConfigureAwait(false);

        if (!result.Value.Holds)
        {
            throw WebBackendErrors.NotActionable(element, _options.ActionTimeout, result.Value.Observation);
        }
    }

    public ValueTask FillAsync(WebElementReference element, string value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(value);
        return ValueTask.CompletedTask;
    }

    public ValueTask CheckAsync(WebElementReference element, bool isChecked, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    public ValueTask SelectOptionAsync(WebElementReference element, string value, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    public ValueTask PressAsync(WebElementReference element, WebKey key, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    public ValueTask<int> CountAsync(WebElementReference elements, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(1);

    public ValueTask<string> ReadTextAsync(WebElementReference element, CancellationToken cancellationToken = default)
        => ValueTask.FromResult("minimal");

    public ValueTask<string?> ReadValueAsync(WebElementReference element, CancellationToken cancellationToken = default)
        => ValueTask.FromResult<string?>(null);

    public ValueTask<bool> IsVisibleAsync(WebElementReference element, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(true);

    public ValueTask<bool> IsEnabledAsync(WebElementReference element, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(true);

    public ValueTask<bool> IsCheckedAsync(WebElementReference element, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(true);

    /// <summary>Captures the documented failure artifacts from canned observations.</summary>
    public async ValueTask<IReadOnlyList<ProtoTestAttachment>> CaptureFailureAsync(
        WebFailureContext failure,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return await WebFailureArtifacts.CaptureAsync(
            _context,
            TraceSource,
            Name,
            _sessionName,
            failure,
            sequence: 1,
            screenshot: () => ValueTask.FromResult<byte[]?>(Encoding.UTF8.GetBytes("minimal-png")),
            dom: () => ValueTask.FromResult<string?>("<html><body>minimal</body></html>"),
            location: () => ValueTask.FromResult<(string? Url, string? Title)>((_address, "Minimal page"))).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
