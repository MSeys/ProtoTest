namespace ProtoTest.Web.Playwright;

using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using ProtoTest.Core;
using ProtoTest.Web.Internal;

public sealed partial class PlaywrightWebBackend : IWebBackend, IWebBackendJavaScript, IWebBackendDiagnostics, IWebBackendDownloads
{
    /// <summary>The trace source every Playwright backend event declares.</summary>
    internal const string TraceSource = "ProtoTest.Web.Playwright";

    private readonly ProtoExecutionContext _context;
    private readonly IBrowserContext _browserContext;
    private readonly PlaywrightWebOptions _options;
    private readonly string _sessionName;
    private readonly PlaywrightCorrelationState _correlation = new();
    private readonly PlaywrightLocatorTranslator _locators;
    private int _failureSequence;
    private bool _webFailure;
    private int _completeStarted;
    private int _disposeStarted;

    private PlaywrightWebBackend(
        ProtoExecutionContext context,
        IBrowserContext browserContext,
        IPage page,
        PlaywrightWebOptions options,
        string sessionName)
    {
        _context = context;
        _browserContext = browserContext;
        Page = page;
        _options = options;
        _sessionName = sessionName;
        _locators = new PlaywrightLocatorTranslator(page);
        // Playwright's default is 30 seconds; the assertion polling contract needs one read or action to
        // give up inside its own budget, and Selenium already fails after ActionTimeout.
        Page.SetDefaultTimeout((float)_options.ActionTimeout.TotalMilliseconds);
        WireDiagnostics();
    }

    public string Name => "Playwright";
    public IPage Page { get; }
    public IBrowserContext BrowserContext => _browserContext;

    public ValueTask<string?> GetCurrentAddressAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult<string?>(Page.Url);

    /// <summary>
    /// Starts native trace correlation for a semantic operation. Grouping follows operation lineage: an
    /// operation joins the trace group of its explicit nesting scope (a <c>WaitUntilAsync</c> condition)
    /// instead of waiting on the gate that scope still holds. A second top-level operation started while
    /// another is in flight is not nested, and a fire-and-forget operation cannot leave a stale
    /// correlation behind because nothing is stored per flow.
    /// </summary>
    public ValueTask BeginOperationAsync(
        WebBackendOperationContext operation,
        CancellationToken cancellationToken = default)
    {
        if (operation.ParentCorrelationId is { } parent && _correlation.IsOpen(parent))
        {
            // A nested operation never owns a trace group - its group-owning ancestor holds the gate -
            // but it is tracked while it runs so its own descendants keep resolving the lineage at any
            // depth. Without the registration a read inside a nested WaitUntil would look top-level and
            // start a group while the outer wait still holds the gate, deadlocking against itself.
            _correlation.Open(operation.CorrelationId);
            return ValueTask.CompletedTask;
        }

        _correlation.Open(operation.CorrelationId);
        if (_options.TraceRetention == PlaywrightTraceRetention.Off || !_options.CorrelateTraceGroups)
        {
            return ValueTask.CompletedTask;
        }

        return BeginTraceGroupAsync(operation, cancellationToken);
    }

    private async ValueTask BeginTraceGroupAsync(
        WebBackendOperationContext operation,
        CancellationToken cancellationToken)
    {
        await _correlation.TraceGroupGate.WaitAsync(cancellationToken);
        try
        {
            await _browserContext.Tracing.GroupAsync(
                $"[{operation.CorrelationId}] [{operation.SessionName}] {operation.Name}");
            _correlation.OpenTraceGroup(operation.CorrelationId);
        }
        catch (Exception exception)
        {
            _correlation.TraceGroupGate.Release();
            TraceDiagnosticFailure("group_start", exception, operation.CorrelationId);
        }
    }

    public ValueTask EndOperationAsync(
        WebBackendOperationContext operation,
        ProtoTraceOutcome outcome,
        Exception? exception = null,
        CancellationToken cancellationToken = default)
    {
        _correlation.Close(operation.CorrelationId);

        // Only the operation that opened the group ends it.
        if (!_correlation.CloseTraceGroup(operation.CorrelationId)) return ValueTask.CompletedTask;
        return EndTraceGroupAsync(operation);
    }

    private async ValueTask EndTraceGroupAsync(WebBackendOperationContext operation)
    {
        try
        {
            await _browserContext.Tracing.GroupEndAsync();
        }
        catch (Exception groupException)
        {
            TraceDiagnosticFailure("group_end", groupException, operation.CorrelationId);
        }
        finally
        {
            _correlation.TraceGroupGate.Release();
        }
    }

    internal static async ValueTask<PlaywrightWebBackend> CreateAsync(
        ProtoExecutionContext context,
        PlaywrightBrowserPool pool,
        PlaywrightWebOptions options,
        string sessionName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IBrowserContext? browserContext = null;
        try
        {
            var browser = await pool.GetBrowserAsync(options, cancellationToken);
            browserContext = await browser.NewContextAsync(options.BuildContextOptions());
            if (options.TraceRetention != PlaywrightTraceRetention.Off)
            {
                await browserContext.Tracing.StartAsync(new TracingStartOptions
                {
                    Screenshots = true,
                    Snapshots = true,
                    Sources = true
                });
            }

            var page = await browserContext.NewPageAsync();
            return new PlaywrightWebBackend(context, browserContext, page, options, sessionName);
        }
        catch
        {
            if (browserContext is not null) await browserContext.DisposeAsync();
            throw;
        }
    }

    public async ValueTask NavigateAsync(Uri address, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Page.GotoAsync(address.ToString());
    }

    public async ValueTask ClickAsync(WebElementReference element, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await ExecuteResolvedAsync(element, locator => locator.ClickAsync());
    }

    public async ValueTask FillAsync(WebElementReference element, string value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await ExecuteResolvedAsync(element, locator => locator.FillAsync(value));
    }

    public async ValueTask CheckAsync(WebElementReference element, bool isChecked, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await ExecuteResolvedAsync(element, locator => isChecked ? locator.CheckAsync() : locator.UncheckAsync());
    }

    public async ValueTask SelectOptionAsync(WebElementReference element, string value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Value only, so the label of another option can never satisfy the selection: that is the
        // documented contract and the behavior Selenium already implements.
        await ExecuteResolvedAsync(element, locator => locator.SelectOptionAsync(new SelectOptionValue { Value = value }));
    }

    public async ValueTask PressAsync(WebElementReference element, WebKey key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await ExecuteResolvedAsync(element, locator => locator.PressAsync(PlaywrightLocatorTranslator.MapKey(key)));
    }

    public async ValueTask<int> CountAsync(WebElementReference elements, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _locators.Resolve(elements).CountAsync();
    }

    public async ValueTask<string> ReadTextAsync(WebElementReference element, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await ReadResolvedAsync(element, locator => locator.InnerTextAsync());
    }

    public async ValueTask<string?> ReadValueAsync(WebElementReference element, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await ReadResolvedAsync(element, locator => locator.InputValueAsync());
    }

    public ValueTask<bool> IsVisibleAsync(WebElementReference element, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ReadFlagAsync(element, locator => locator.IsVisibleAsync());
    }

    public ValueTask<bool> IsEnabledAsync(WebElementReference element, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ReadFlagAsync(element, locator => locator.IsEnabledAsync());
    }

    public ValueTask<bool> IsCheckedAsync(WebElementReference element, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ReadFlagAsync(element, locator => locator.IsCheckedAsync());
    }

    /// <summary>
    /// Reads one boolean element state, rejecting a locator that matches more than one element the same
    /// way the other operations do, and treating "no element" as false rather than an error.
    /// </summary>
    private async ValueTask<bool> ReadFlagAsync(WebElementReference element, Func<ILocator, Task<bool>> read)
    {
        var locator = _locators.Resolve(element);
        var count = await locator.CountAsync();
        if (count > 1) throw WebBackendErrors.MultipleMatch(element.Locator, element.ComponentPath, count);
        return count == 1 && await read(locator);
    }

    public async ValueTask<bool> EvaluateBooleanAsync(string script, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Page.EvaluateAsync<bool>(script);
    }

    public async ValueTask<string?> EvaluateJsonAsync(string script, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Page.EvaluateAsync<string?>(script);
    }

    /// <summary>
    /// Runs the trigger and waits for the download it starts with Playwright's own waiter, then reads
    /// the completed file and its suggested name.
    /// </summary>
    public async ValueTask<WebDownload> DownloadAsync(
        Func<CancellationToken, Task> trigger,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        cancellationToken.ThrowIfCancellationRequested();
        var options = new PageRunAndWaitForDownloadOptions
        {
            // A download keeps Playwright's own 30-second default; the session timeout bounds element
            // waits, not a file the application is still producing.
            Timeout = (float)(timeout ?? TimeSpan.FromSeconds(30)).TotalMilliseconds
        };
        var download = await Page.RunAndWaitForDownloadAsync(() => trigger(cancellationToken), options);
        var fileName = download.SuggestedFilename;
        var path = await download.PathAsync();
        var content = await File.ReadAllBytesAsync(path, cancellationToken);
        return new WebDownload(fileName, WebMediaTypes.Guess(fileName), content);
    }

    public async ValueTask<IReadOnlyList<ProtoTestAttachment>> CaptureFailureAsync(
        WebFailureContext failure,
        CancellationToken cancellationToken = default)
    {
        _webFailure = true;
        cancellationToken.ThrowIfCancellationRequested();
        return await WebFailureArtifacts.CaptureAsync(
            _context,
            TraceSource,
            "Playwright",
            _sessionName,
            failure,
            Interlocked.Increment(ref _failureSequence),
            async () => await Page.ScreenshotAsync(new PageScreenshotOptions { FullPage = true }),
            async () => await Page.ContentAsync(),
            async () => (CurrentLocation(), await Page.TitleAsync()));
    }

    /// <summary>
    /// The sanitized current address; a value that is not an absolute address (for example
    /// <c>about:blank</c>) passes through, so the location artifact is never dropped.
    /// </summary>
    private string? CurrentLocation() => ProtoUriSanitizer.ForDisplay(Page.Url);

    /// <summary>
    /// Runs one action against the resolved locator, translating Playwright's strict-mode violation into
    /// the same <see cref="WebElementResolutionException"/> Selenium raises for multiple matches, and its
    /// auto-wait timeout into the same <see cref="WebActionabilityException"/> Selenium raises after
    /// <see cref="PlaywrightWebOptions.ActionTimeout"/>. Without the translation, polling assertions and
    /// wait conditions would see a raw <c>PlaywrightException</c> instead of the documented failures.
    /// </summary>
    private ValueTask<bool> ExecuteResolvedAsync(WebElementReference element, Func<ILocator, Task> action)
        => RunResolvedAsync(
            element,
            async locator =>
            {
                await action(locator);
                return true;
            },
            element => WebBackendErrors.NotActionable(element, _options.ActionTimeout));

    private ValueTask<T> ReadResolvedAsync<T>(WebElementReference element, Func<ILocator, Task<T>> read)
        => RunResolvedAsync(element, read, element => WebBackendErrors.NotPresent(element, _options.ActionTimeout));

    /// <summary>
    /// Runs one operation against the resolved locator, translating Playwright's strict-mode violation
    /// into the same <see cref="WebElementResolutionException"/> Selenium raises for multiple matches and
    /// its auto-wait timeout into the failure the caller names.
    /// </summary>
    private async ValueTask<T> RunResolvedAsync<T>(
        WebElementReference element,
        Func<ILocator, Task<T>> action,
        Func<WebElementReference, Exception> onTimeout)
    {
        var locator = _locators.Resolve(element);
        try
        {
            return await action(locator);
        }
        catch (TimeoutException)
        {
            throw onTimeout(element);
        }
        catch (PlaywrightException exception) when (IsStrictViolation(exception))
        {
            throw WebBackendErrors.MultipleMatch(element.Locator, element.ComponentPath, null);
        }
    }

    private static bool IsStrictViolation(PlaywrightException exception)
        => exception.Message.Contains("strict mode violation", StringComparison.OrdinalIgnoreCase);

    public async ValueTask CompleteAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _completeStarted, 1) != 0) return;
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (_options.TraceRetention != PlaywrightTraceRetention.Off)
            {
                var retain = _options.TraceRetention == PlaywrightTraceRetention.Always || _webFailure;
                if (retain)
                {
                    var path = Path.Combine(Path.GetTempPath(), $"prototest-playwright-{Guid.NewGuid():N}.zip");
                    try
                    {
                        await _browserContext.Tracing.StopAsync(new TracingStopOptions { Path = path });
                        var bytes = await PlaywrightTraceFile.ReadAsync(path, _options.MaxTraceBytes, cancellationToken);
                        if (bytes is null)
                        {
                            // The cap is a deliberate artifact policy, not a capture failure: the trace
                            // stays on disk for manual inspection and the trace says why it was skipped.
                            _context.Trace.WriteEvent(
                                "web.playwright.trace_too_large",
                                "Playwright native trace exceeded the configured cap",
                                TraceSource,
                                ProtoTracePhase.Teardown,
                                ProtoTraceOutcome.Failed,
                                attributes: new Dictionary<string, string?>
                                {
                                    ["web.trace.limit_bytes"] = _options.MaxTraceBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                    ["web.trace.bytes"] = new FileInfo(path).Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
                                });
                        }
                        else
                        {
                            _context.AddAttachment(ProtoTestAttachment.FromBytes(
                                $"playwright-{WebNames.SafeName(_sessionName)}-trace.zip",
                                bytes,
                                "application/vnd.microsoft.playwright.trace+zip",
                                $"Native Playwright trace for Web session '{_sessionName}'."));
                        }
                    }
                    finally
                    {
                        try { if (File.Exists(path)) File.Delete(path); }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                }
                else
                {
                    await _browserContext.Tracing.StopAsync();
                }
            }
        }
        catch (Exception exception)
        {
            _context.Trace.WriteEvent(
                "web.playwright.trace_failed",
                "Playwright native trace capture failed",
                TraceSource,
                ProtoTracePhase.Teardown,
                ProtoTraceOutcome.Failed,
                exception: exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0) return;
        await CompleteAsync();
        _correlation.TraceGroupGate.Dispose();
        await _browserContext.DisposeAsync();
    }
}

internal sealed class PlaywrightWebBackendFactory(Action<PlaywrightWebOptions>? configure) : IWebBackendFactory
{
    public string Name => PlaywrightWebOptions.BackendName;

    public async ValueTask<IWebBackend> CreateAsync(
        ProtoExecutionContext context,
        string sessionName,
        CancellationToken cancellationToken = default)
    {
        var options = WebBackendOptions.Resolve(context, configure, PlaywrightWebOptions.Validate);
        return await PlaywrightWebBackend.CreateAsync(
            context,
            context.Service<PlaywrightBrowserPool>(),
            options,
            sessionName,
            cancellationToken);
    }
}
