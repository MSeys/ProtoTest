namespace ProtoTest.Web.Selenium;

using System.Collections.Concurrent;
using System.Text.Json;
using OpenQA.Selenium;
using ProtoTest.Core;
using ProtoTest.Web.Internal;

public sealed partial class SeleniumWebBackend : IWebBackend, IWebBackendJavaScript, IWebBackendDiagnostics
{
    /// <summary>The trace source every Selenium backend event declares.</summary>
    internal const string TraceSource = "ProtoTest.Web.Selenium";

    private readonly ProtoExecutionContext _context;
    private readonly SeleniumWebOptions _options;
    private readonly string _sessionName;
    private readonly SeleniumDriverExecutor _executor;
    private readonly WebProbeLoop _probes;
    private readonly ConcurrentQueue<SeleniumDiagnosticEntry> _diagnostics = new();
    private readonly DateTimeOffset _startedAtUtc = DateTimeOffset.UtcNow;
    private int _failureSequence;
    private bool _webFailure;
    private int _completeStarted;
    private int _disposeStarted;

    internal SeleniumWebBackend(
        ProtoExecutionContext context,
        IWebDriver driver,
        SeleniumWebOptions options,
        string sessionName,
        TimeSpan? pumpJoinTimeout = null)
    {
        _context = context;
        Driver = driver;
        _options = options;
        _sessionName = sessionName;
        _executor = new SeleniumDriverExecutor(pumpJoinTimeout);
        _probes = new WebProbeLoop(() => _options.PollInterval);
    }

    public string Name => "Selenium";
    public IWebDriver Driver { get; }

    /// <summary>The session assertion poll interval is this option, so one setting retimes the backend's
    /// action retries and the session's element assertions together.</summary>
    public TimeSpan PollInterval => _options.PollInterval;

    public ValueTask<string?> GetCurrentAddressAsync(CancellationToken cancellationToken = default)
        => _executor.RunAsync<string?>(() => Driver.Url, cancellationToken);

    public ValueTask NavigateAsync(Uri address, CancellationToken cancellationToken = default)
        => _executor.RunAsync(() => Driver.Navigate().GoToUrl(address), cancellationToken);

    public ValueTask ClickAsync(WebElementReference element, CancellationToken cancellationToken = default)
        => ExecuteActionableAsync(element, WebOperationKind.Click, resolved =>
        {
            resolved.Click();
            return null;
        }, cancellationToken);

    public ValueTask FillAsync(WebElementReference element, string value, CancellationToken cancellationToken = default)
        => ExecuteActionableAsync(element, WebOperationKind.Fill, resolved =>
        {
            resolved.Clear();
            resolved.SendKeys(value);
            return null;
        }, cancellationToken);

    public ValueTask CheckAsync(WebElementReference element, bool isChecked, CancellationToken cancellationToken = default)
        => ExecuteActionableAsync(element, WebOperationKind.Check, resolved =>
        {
            if (resolved.Selected != isChecked)
            {
                resolved.Click();
                if (resolved.Selected != isChecked)
                {
                    return $"checked={resolved.Selected.ToString().ToLowerInvariant()} after the click, " +
                           $"expected checked={isChecked.ToString().ToLowerInvariant()}";
                }
            }

            return null;
        }, cancellationToken);

    public ValueTask SelectOptionAsync(WebElementReference element, string value, CancellationToken cancellationToken = default)
        => ExecuteActionableAsync(element, WebOperationKind.SelectOption, resolved =>
        {
            var matches = resolved.FindElements(OpenQA.Selenium.By.TagName("option"))
                .Where(option => string.Equals(option.GetDomProperty("value"), value, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1)
                throw new WebElementResolutionException(
                    $"Expected one option with the requested value in '{element.ComponentPath}.{element.Name}', but found {matches.Length}.");
            matches[0].Click();
            return matches[0].Selected
                ? null
                : $"option with value '{value}' is not selected after the click";
        }, cancellationToken);

    public ValueTask PressAsync(WebElementReference element, WebKey key, CancellationToken cancellationToken = default)
        => ExecuteActionableAsync(element, WebOperationKind.Press, resolved =>
        {
            resolved.SendKeys(MapKey(key));
            return null;
        }, cancellationToken);

    public ValueTask<int> CountAsync(WebElementReference elements, CancellationToken cancellationToken = default)
        => _executor.RunAsync(
            () => ResolveMany(
                ResolveScope(elements),
                elements.Locator,
                elements.ComponentPath,
                throwOnMissing: false).Count,
            cancellationToken);

    public async ValueTask<string> ReadTextAsync(WebElementReference element, CancellationToken cancellationToken = default)
        => (await ResolvePresentAsync(element, cancellationToken)).Text;

    public async ValueTask<string?> ReadValueAsync(WebElementReference element, CancellationToken cancellationToken = default)
        => (await ResolvePresentAsync(element, cancellationToken)).GetDomProperty("value");

    public ValueTask<bool> IsVisibleAsync(WebElementReference element, CancellationToken cancellationToken = default)
        => _executor.RunAsync(() => TryInspect(element, resolved => resolved.Displayed), cancellationToken);

    public ValueTask<bool> IsEnabledAsync(WebElementReference element, CancellationToken cancellationToken = default)
        => _executor.RunAsync(() => TryInspect(element, resolved => resolved.Enabled), cancellationToken);

    public ValueTask<bool> IsCheckedAsync(WebElementReference element, CancellationToken cancellationToken = default)
        => _executor.RunAsync(() => TryInspect(element, resolved => resolved.Selected), cancellationToken);

    public ValueTask<bool> EvaluateBooleanAsync(string script, CancellationToken cancellationToken = default)
        => _executor.RunAsync(
            () =>
            {
                if (Driver is not IJavaScriptExecutor javascript)
                    throw new WebBackendCapabilityException(
                        $"Selenium driver '{Driver.GetType().FullName}' does not support JavaScript execution.");
                return Convert.ToBoolean(javascript.ExecuteScript($"return Boolean({script});"));
            },
            cancellationToken);

    public ValueTask<string?> EvaluateJsonAsync(string script, CancellationToken cancellationToken = default)
        => _executor.RunAsync(
            () =>
            {
                if (Driver is not IJavaScriptExecutor javascript)
                    throw new WebBackendCapabilityException(
                        $"Selenium driver '{Driver.GetType().FullName}' does not support JavaScript execution.");
                return javascript.ExecuteScript($"return ({script});")?.ToString();
            },
            cancellationToken);

    /// <summary>
    /// Selenium has no download API: the WebDriver protocol gives a driver no way to observe or fetch
    /// a browser download. The backend does not implement <see cref="IWebBackendDownloads"/>, so
    /// <c>WebSession.DownloadAsync</c> reports the capability it actually lacks.
    /// </summary>

    public ValueTask<IReadOnlyList<ProtoTestAttachment>> CaptureFailureAsync(
        WebFailureContext failure,
        CancellationToken cancellationToken = default)
    {
        _webFailure = true;
        return WebFailureArtifacts.CaptureAsync(
            _context,
            TraceSource,
            "Selenium",
            _sessionName,
            failure,
            Interlocked.Increment(ref _failureSequence),
            () => _executor.RunAsync<byte[]?>(
                () => Driver is ITakesScreenshot screenshots ? screenshots.GetScreenshot().AsByteArray : null,
                cancellationToken),
            () => _executor.RunAsync<string?>(() => Driver.PageSource, cancellationToken),
            () => _executor.RunAsync<(string? Url, string? Title)>(() => (Location(), Driver.Title), cancellationToken));
    }

    private string? Location() => ProtoUriSanitizer.ForDisplay(Driver.Url);
}

internal sealed class SeleniumWebBackendFactory(
    Func<IWebDriver> createDriver,
    Action<SeleniumWebOptions>? configure) : IWebBackendFactory
{
    public string Name => SeleniumWebOptions.BackendName;

    public ValueTask<IWebBackend> CreateAsync(
        ProtoExecutionContext context,
        string sessionName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var options = WebBackendOptions.Resolve(context, configure, SeleniumWebOptions.Validate);
        return ValueTask.FromResult<IWebBackend>(
            new SeleniumWebBackend(context, createDriver(), options, sessionName));
    }
}
