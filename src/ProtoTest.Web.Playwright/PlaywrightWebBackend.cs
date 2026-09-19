namespace ProtoTest.Web.Playwright;

using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using ProtoTest.Core;
using ProtoTest.Web.Internal;

public sealed class PlaywrightWebBackend : IWebBackend, IWebBackendJavaScript, IWebBackendDiagnostics, IWebBackendDownloads
{
    private readonly ProtoExecutionContext _context;
    private readonly IBrowserContext _browserContext;
    private readonly PlaywrightWebOptions _options;
    private readonly string _sessionName;
    private readonly SemaphoreSlim _traceGroupGate = new(1, 1);
    private readonly ConcurrentDictionary<string, byte> _openTraceGroups = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _openOperations = new(StringComparer.Ordinal);
    private string? _activeCorrelation;
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
        WireDiagnostics();
    }

    public string Name => "Playwright";
    public IPage Page { get; }
    public IBrowserContext BrowserContext => _browserContext;

    public string? CurrentAddress => Page.Url;

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
        if (operation.ParentCorrelationId is { } parent && _openOperations.ContainsKey(parent))
        {
            // A nested operation never owns a trace group - its group-owning ancestor holds the gate -
            // but it is tracked while it runs so its own descendants keep resolving the lineage at any
            // depth. Without the registration a read inside a nested WaitUntil would look top-level and
            // start a group while the outer wait still holds the gate, deadlocking against itself.
            _openOperations[operation.CorrelationId] = 0;
            return ValueTask.CompletedTask;
        }

        _openOperations[operation.CorrelationId] = 0;
        Volatile.Write(ref _activeCorrelation, operation.CorrelationId);
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
        await _traceGroupGate.WaitAsync(cancellationToken);
        try
        {
            await _browserContext.Tracing.GroupAsync(
                $"[{operation.CorrelationId}] [{operation.SessionName}] {operation.Name}");
            _openTraceGroups[operation.CorrelationId] = 0;
        }
        catch (Exception exception)
        {
            _traceGroupGate.Release();
            TraceDiagnosticFailure("group_start", exception, operation.CorrelationId);
        }
    }

    public ValueTask EndOperationAsync(
        WebBackendOperationContext operation,
        ProtoTraceOutcome outcome,
        Exception? exception = null,
        CancellationToken cancellationToken = default)
    {
        _openOperations.TryRemove(operation.CorrelationId, out _);
        if (string.Equals(Volatile.Read(ref _activeCorrelation), operation.CorrelationId, StringComparison.Ordinal))
        {
            Volatile.Write(ref _activeCorrelation, null);
        }

        // Only the operation that opened the group ends it.
        if (!_openTraceGroups.TryRemove(operation.CorrelationId, out _)) return ValueTask.CompletedTask;
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
            _traceGroupGate.Release();
        }
    }

    /// <summary>The most recently opened operation on this session, used to parent native diagnostics.</summary>
    private string? ActiveCorrelation => Volatile.Read(ref _activeCorrelation);

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
            browserContext = await browser.NewContextAsync(options.Context);
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
        await ExecuteResolvedAsync(element, locator => locator.SelectOptionAsync(value));
    }

    public async ValueTask PressAsync(WebElementReference element, WebKey key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await ExecuteResolvedAsync(element, locator => locator.PressAsync(MapKey(key)));
    }

    public async ValueTask<int> CountAsync(WebElementReference elements, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Resolve(elements).CountAsync();
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

    public async ValueTask<bool> IsVisibleAsync(WebElementReference element, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var locator = Resolve(element);
        var count = await locator.CountAsync();
        if (count > 1)
            throw new WebElementResolutionException(
                $"Expected at most one element for {element.Locator.Describe()} in {element.ComponentPath}, but found {count}.");
        return count == 1 && await locator.IsVisibleAsync();
    }

    public async ValueTask<bool> IsEnabledAsync(WebElementReference element, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var locator = Resolve(element);
        var count = await locator.CountAsync();
        if (count > 1) throw MultipleMatch(element, count);
        return count == 1 && await locator.IsEnabledAsync();
    }

    public async ValueTask<bool> IsCheckedAsync(WebElementReference element, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var locator = Resolve(element);
        var count = await locator.CountAsync();
        if (count > 1) throw MultipleMatch(element, count);
        return count == 1 && await locator.IsCheckedAsync();
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
        var options = new PageRunAndWaitForDownloadOptions();
        if (timeout is { } wait)
        {
            options.Timeout = (float)wait.TotalMilliseconds;
        }

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
            "ProtoTest.Web.Playwright",
            "Playwright",
            _sessionName,
            failure,
            Interlocked.Increment(ref _failureSequence),
            async prefix => ProtoTestAttachment.FromBytes(
                $"web-{prefix}-failure.png",
                await Page.ScreenshotAsync(new PageScreenshotOptions { FullPage = true }),
                "image/png",
                "Playwright page at web operation failure."),
            async prefix => ProtoTestAttachment.FromText(
                $"web-{prefix}-page.html",
                await Page.ContentAsync(),
                "text/html",
                "DOM snapshot at web operation failure."),
            prefix => ValueTask.FromResult<ProtoTestAttachment?>(ProtoTestAttachment.FromText(
                $"web-{prefix}-location.txt",
                CurrentLocation(),
                "text/plain",
                "URL at web operation failure.")));
    }

    /// <summary>
    /// The sanitized current address, falling back to the raw address when sanitizing produces nothing
    /// (for example <c>about:blank</c>), so the location artifact is never dropped.
    /// </summary>
    private string CurrentLocation()
    {
        var address = Page.Url;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)) return address;
        return ProtoUriSanitizer.Sanitize(uri, null) ?? address;
    }

    /// <summary>
    /// Runs one action against the resolved locator, translating Playwright's strict-mode violation into
    /// the same <see cref="WebElementResolutionException"/> Selenium raises for multiple matches. Without
    /// the translation, polling assertions would see a raw <c>PlaywrightException</c> instead of the
    /// documented resolution failure.
    /// </summary>
    private async ValueTask ExecuteResolvedAsync(WebElementReference element, Func<ILocator, Task> action)
    {
        var locator = Resolve(element);
        try
        {
            await action(locator);
        }
        catch (PlaywrightException exception) when (IsStrictViolation(exception))
        {
            throw MultipleMatch(element);
        }
    }

    private async ValueTask<T> ReadResolvedAsync<T>(WebElementReference element, Func<ILocator, Task<T>> read)
    {
        var locator = Resolve(element);
        try
        {
            return await read(locator);
        }
        catch (PlaywrightException exception) when (IsStrictViolation(exception))
        {
            throw MultipleMatch(element);
        }
    }

    private static bool IsStrictViolation(PlaywrightException exception)
        => exception.Message.Contains("strict mode violation", StringComparison.OrdinalIgnoreCase);

    private ILocator Resolve(WebElementReference element)
    {
        ILocator? current = null;
        foreach (var root in element.ComponentRoots)
            current = Apply(current, root);
        return Apply(current, element.Locator);
    }

    private ILocator Apply(ILocator? scope, WebLocator locator)
        => locator switch
        {
            TestIdWebLocator value => scope is null ? Page.GetByTestId(value.Value) : scope.GetByTestId(value.Value),
            RoleWebLocator value => Role(scope, value),
            TextWebLocator value => Text(scope, value),
            LabelWebLocator value => scope is null
                ? Page.GetByLabel(value.Value, new PageGetByLabelOptions { Exact = value.Exact })
                : scope.GetByLabel(value.Value, new LocatorGetByLabelOptions { Exact = value.Exact }),
            PlaceholderWebLocator value => scope is null
                ? Page.GetByPlaceholder(value.Value, new PageGetByPlaceholderOptions { Exact = value.Exact })
                : scope.GetByPlaceholder(value.Value, new LocatorGetByPlaceholderOptions { Exact = value.Exact }),
            CssWebLocator value => scope is null ? Page.Locator(value.Selector) : scope.Locator(value.Selector),
            AttributeWebLocator value => scope is null
                ? Page.Locator($"[{CssIdentifier(value.Name)}={CssString(value.Value)}]")
                : scope.Locator($"[{CssIdentifier(value.Name)}={CssString(value.Value)}]"),
            NthWebLocator value => Apply(scope, value.Source).Nth(value.Index),
            TableCellWebLocator value => (scope is null
                ? Page.Locator("th, td")
                : scope.Locator(":scope > th, :scope > td")).Nth(value.Index),
            TableCellByHeaderWebLocator value => scope is null
                ? Page.Locator($"xpath={WebXPath.TableCellByHeader(value, documentScoped: true)}")
                : scope.Locator($"xpath={WebXPath.TableCellByHeader(value)}"),
            AndWebLocator value => And(scope, value),
            HasTextWebLocator => throw new WebBackendCapabilityException("HasText is a filter and must be composed with another locator using And()."),
            _ => throw new WebBackendCapabilityException($"Playwright does not support locator type '{locator.GetType().Name}'.")
        };

    private ILocator Role(ILocator? scope, RoleWebLocator locator)
    {
        var role = MapRole(locator.Role);
        if (scope is null)
        {
            var options = new PageGetByRoleOptions { Exact = locator.Exact };
            if (locator.Name is not null) options.Name = locator.Name;
            return Page.GetByRole(role, options);
        }
        else
        {
            var options = new LocatorGetByRoleOptions { Exact = locator.Exact };
            if (locator.Name is not null) options.Name = locator.Name;
            return scope.GetByRole(role, options);
        }
    }

    private ILocator Text(ILocator? scope, TextWebLocator locator)
    {
        // A Playwright string is case-insensitive unless Exact; only the exact case-sensitive case can
        // use a plain string, the rest need a regex with matching flags.
        if (!locator.IgnoreCase && locator.Exact)
        {
            return scope is null
                ? Page.GetByText(locator.Value, new PageGetByTextOptions { Exact = true })
                : scope.GetByText(locator.Value, new LocatorGetByTextOptions { Exact = true });
        }

        var pattern = locator.Exact ? $"^{Regex.Escape(locator.Value)}$" : Regex.Escape(locator.Value);
        var regex = new Regex(pattern, locator.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
        return scope is null ? Page.GetByText(regex) : scope.GetByText(regex);
    }

    private ILocator And(ILocator? scope, AndWebLocator locator)
    {
        var left = Apply(scope, locator.Left);
        if (locator.Right is HasTextWebLocator text)
        {
            if (text.IgnoreCase && !text.Exact)
                return left.Filter(new LocatorFilterOptions { HasText = text.Value });
            var pattern = text.Exact ? $"^{Regex.Escape(text.Value)}$" : Regex.Escape(text.Value);
            return left.Filter(new LocatorFilterOptions
            {
                HasTextRegex = new Regex(pattern, text.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None)
            });
        }

        return left.And(Apply(scope, locator.Right));
    }

    private static AriaRole MapRole(WebRole role) => role switch
    {
        WebRole.Alert => AriaRole.Alert,
        WebRole.Button => AriaRole.Button,
        WebRole.Checkbox => AriaRole.Checkbox,
        WebRole.Combobox => AriaRole.Combobox,
        WebRole.Dialog => AriaRole.Dialog,
        WebRole.Grid => AriaRole.Grid,
        WebRole.Heading => AriaRole.Heading,
        WebRole.Image => AriaRole.Img,
        WebRole.Link => AriaRole.Link,
        WebRole.List => AriaRole.List,
        WebRole.ListItem => AriaRole.Listitem,
        WebRole.Menu => AriaRole.Menu,
        WebRole.MenuItem => AriaRole.Menuitem,
        WebRole.Navigation => AriaRole.Navigation,
        WebRole.Option => AriaRole.Option,
        WebRole.ProgressBar => AriaRole.Progressbar,
        WebRole.Radio => AriaRole.Radio,
        WebRole.Region => AriaRole.Region,
        WebRole.Row => AriaRole.Row,
        WebRole.RowGroup => AriaRole.Rowgroup,
        WebRole.Searchbox => AriaRole.Searchbox,
        WebRole.Slider => AriaRole.Slider,
        WebRole.SpinButton => AriaRole.Spinbutton,
        WebRole.Status => AriaRole.Status,
        WebRole.Switch => AriaRole.Switch,
        WebRole.Tab => AriaRole.Tab,
        WebRole.Table => AriaRole.Table,
        WebRole.TabList => AriaRole.Tablist,
        WebRole.TabPanel => AriaRole.Tabpanel,
        WebRole.Textbox => AriaRole.Textbox,
        WebRole.Toolbar => AriaRole.Toolbar,
        WebRole.Tooltip => AriaRole.Tooltip,
        WebRole.Tree => AriaRole.Tree,
        WebRole.TreeItem => AriaRole.Treeitem,
        _ => throw new WebBackendCapabilityException($"Playwright role mapping is not available for '{role}'.")
    };

    private static string MapKey(WebKey key) => WebKeyMap.Get(key).Playwright;

    private static WebElementResolutionException MultipleMatch(WebElementReference element, int count)
        => new($"Expected at most one element for {element.Locator.Describe()} in {element.ComponentPath}, but found {count}.");

    private static WebElementResolutionException MultipleMatch(WebElementReference element)
        => new($"Expected at most one element for {element.Locator.Describe()} in {element.ComponentPath}, but Playwright reported a strict mode violation (more than one element matched).");

    private static string CssIdentifier(string value)
    {
        if (value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or ':'))
            return value.Replace(":", "\\:");
        throw new WebBackendCapabilityException($"Attribute name '{value}' cannot be represented safely as a CSS identifier.");
    }

    private static string CssString(string value) => $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

    private void WireDiagnostics()
    {
        if (_options.ConsoleCapture != PlaywrightConsoleCapture.Off)
        {
            Page.Console += (_, message) =>
            {
                if (!ShouldCaptureConsole(message.Type)) return;
                _context.Trace.WriteEvent(
                    "web.browser.console",
                    $"Browser console · {message.Type}",
                    "ProtoTest.Web.Playwright",
                    outcome: ProtoTraceOutcome.Unknown,
                    attributes: new Dictionary<string, string?>
                    {
                        ["web.session"] = _sessionName,
                        ["web.correlation_id"] = ActiveCorrelation,
                        ["browser.console.type"] = message.Type,
                        ["browser.console.text"] = Truncate(message.Text)
                    },
                    parentId: ActiveCorrelation);
            };
        }

        if (_options.CapturePageErrors)
        {
            Page.PageError += (_, message) => _context.Trace.WriteEvent(
                "web.browser.page_error",
                "Browser page error",
                "ProtoTest.Web.Playwright",
                outcome: ProtoTraceOutcome.Unknown,
                attributes: new Dictionary<string, string?>
                {
                    ["web.session"] = _sessionName,
                    ["web.correlation_id"] = ActiveCorrelation,
                    ["browser.error.message"] = Truncate(message)
                },
                parentId: ActiveCorrelation);
        }

        if (_options.CaptureRequestFailures)
        {
            Page.RequestFailed += (_, request) => _context.Trace.WriteEvent(
                "web.browser.request_failed",
                $"Request failed · {request.Method}",
                "ProtoTest.Web.Playwright",
                outcome: ProtoTraceOutcome.Unknown,
                attributes: new Dictionary<string, string?>
                {
                    ["web.session"] = _sessionName,
                    ["web.correlation_id"] = ActiveCorrelation,
                    ["http.method"] = request.Method,
                    ["http.url"] = SafeUrl(request.Url),
                    ["browser.request.failure"] = Truncate(request.Failure)
                },
                parentId: ActiveCorrelation);
        }
    }

    private bool ShouldCaptureConsole(string type)
        => _options.ConsoleCapture switch
        {
            PlaywrightConsoleCapture.All => true,
            PlaywrightConsoleCapture.Errors => string.Equals(type, "error", StringComparison.OrdinalIgnoreCase),
            PlaywrightConsoleCapture.WarningsAndErrors =>
                string.Equals(type, "error", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "warning", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "warn", StringComparison.OrdinalIgnoreCase),
            _ => false
        };

    private void TraceDiagnosticFailure(string stage, Exception exception, string correlationId)
        => _context.Trace.WriteEvent(
            "web.playwright.correlation_failed",
            $"Playwright trace correlation failed · {stage}",
            "ProtoTest.Web.Playwright",
            outcome: ProtoTraceOutcome.Unknown,
            attributes: new Dictionary<string, string?>
            {
                ["web.session"] = _sessionName,
                ["web.correlation_id"] = correlationId,
                ["web.diagnostics.stage"] = stage,
                ["web.diagnostics.error"] = exception.Message
            },
            parentId: correlationId);

    private static string? SafeUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return value;
        return new UriBuilder(uri) { Query = string.Empty, Fragment = string.Empty }.Uri.ToString();
    }

    private static string? Truncate(string? value)
        => value is null || value.Length <= 4096 ? value : value[..4096] + "…";

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
                        var bytes = await File.ReadAllBytesAsync(path);
                        _context.AddAttachment(ProtoTestAttachment.FromBytes(
                            $"playwright-{WebNames.SafeName(_sessionName)}-trace.zip",
                            bytes,
                            "application/vnd.microsoft.playwright.trace+zip",
                            $"Native Playwright trace for Web session '{_sessionName}'."));
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
                "ProtoTest.Web.Playwright",
                ProtoTracePhase.Teardown,
                ProtoTraceOutcome.Failed,
                exception: exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0) return;
        await CompleteAsync();
        _traceGroupGate.Dispose();
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
        var options = WebBackendOptions.Resolve(context, sessionName, configure);
        return await PlaywrightWebBackend.CreateAsync(
            context,
            context.Service<PlaywrightBrowserPool>(),
            options,
            sessionName,
            cancellationToken);
    }
}
