namespace ProtoTest.Web.Selenium;

using System.Diagnostics;
using System.Collections.Concurrent;
using System.Text.Json;
using OpenQA.Selenium;
using ProtoTest.Core;

public sealed class SeleniumWebBackend : IWebBackend
{
    private readonly ProtoExecutionContext _context;
    private readonly SeleniumWebOptions _options;
    private readonly string _sessionName;
    private readonly AsyncLocal<string?> _activeCorrelation = new();
    private readonly ConcurrentQueue<SeleniumDiagnosticEntry> _diagnostics = new();
    private readonly DateTimeOffset _startedAtUtc = DateTimeOffset.UtcNow;
    private bool _webFailure;
    private int _completeStarted;
    private int _disposeStarted;

    internal SeleniumWebBackend(
        ProtoExecutionContext context,
        IWebDriver driver,
        SeleniumWebOptions options,
        string sessionName)
    {
        _context = context;
        Driver = driver;
        _options = options;
        _sessionName = sessionName;
    }

    public string Name => "Selenium";
    public IWebDriver Driver { get; }

    public ValueTask BeginOperationAsync(
        WebBackendOperationContext operation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _activeCorrelation.Value = operation.CorrelationId;
        return ValueTask.CompletedTask;
    }

    public ValueTask EndOperationAsync(
        WebBackendOperationContext operation,
        ProtoTraceOutcome outcome,
        Exception? exception = null,
        CancellationToken cancellationToken = default)
    {
        _activeCorrelation.Value = null;
        return ValueTask.CompletedTask;
    }

    public ValueTask NavigateAsync(Uri address, CancellationToken cancellationToken = default)
        => Background(() => Driver.Navigate().GoToUrl(address), cancellationToken);

    public ValueTask ClickAsync(WebElementReference element, CancellationToken cancellationToken = default)
        => Background(() => ExecuteActionable(element, WebOperationKind.Click, resolved => resolved.Click(), cancellationToken), cancellationToken);

    public ValueTask FillAsync(WebElementReference element, string value, CancellationToken cancellationToken = default)
        => Background(() =>
        {
            ExecuteActionable(element, WebOperationKind.Fill, resolved =>
            {
                resolved.Clear();
                resolved.SendKeys(value);
            }, cancellationToken);
        }, cancellationToken);

    public ValueTask CheckAsync(WebElementReference element, bool isChecked, CancellationToken cancellationToken = default)
        => Background(() => ExecuteActionable(
            element,
            WebOperationKind.Check,
            resolved =>
            {
                if (resolved.Selected != isChecked) resolved.Click();
            },
            cancellationToken), cancellationToken);

    public ValueTask SelectOptionAsync(WebElementReference element, string value, CancellationToken cancellationToken = default)
        => Background(() => ExecuteActionable(
            element,
            WebOperationKind.SelectOption,
            resolved =>
            {
                var matches = resolved.FindElements(OpenQA.Selenium.By.TagName("option"))
                    .Where(option => string.Equals(option.GetDomProperty("value"), value, StringComparison.Ordinal))
                    .ToArray();
                if (matches.Length != 1)
                    throw new WebElementResolutionException(
                        $"Expected one option with the requested value in '{element.ComponentPath}.{element.Name}', but found {matches.Length}.");
                matches[0].Click();
            },
            cancellationToken), cancellationToken);

    public ValueTask PressAsync(WebElementReference element, WebKey key, CancellationToken cancellationToken = default)
        => Background(() => ExecuteActionable(
            element,
            WebOperationKind.Press,
            resolved => resolved.SendKeys(MapKey(key)),
            cancellationToken), cancellationToken);

    public async ValueTask<int> CountAsync(WebElementReference elements, CancellationToken cancellationToken = default)
        => await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scope = ResolveScope(elements);
            if (elements.Locator is NthWebLocator nth)
            {
                var matches = scope.FindElements(SeleniumLocatorTranslator.Translate(nth.Source));
                return matches.Count > nth.Index ? 1 : 0;
            }
            return scope.FindElements(SeleniumLocatorTranslator.Translate(elements.Locator)).Count;
        }, cancellationToken);

    public async ValueTask<string> ReadTextAsync(WebElementReference element, CancellationToken cancellationToken = default)
        => await Task.Run(() => ResolvePresent(element, cancellationToken).Text, cancellationToken);

    public async ValueTask<string?> ReadValueAsync(WebElementReference element, CancellationToken cancellationToken = default)
        => await Task.Run(() => ResolvePresent(element, cancellationToken).GetDomProperty("value"), cancellationToken);

    public async ValueTask<bool> IsVisibleAsync(WebElementReference element, CancellationToken cancellationToken = default)
        => await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scope = ResolveScope(element);
            try
            {
                return ResolveSingle(scope, element.Locator, element.ComponentPath).Displayed;
            }
            catch (NoSuchElementException)
            {
                return false;
            }
            catch (StaleElementReferenceException)
            {
                return false;
            }
        }, cancellationToken);

    public async ValueTask<bool> IsEnabledAsync(WebElementReference element, CancellationToken cancellationToken = default)
        => await Task.Run(() => TryInspect(element, resolved => resolved.Enabled), cancellationToken);

    public async ValueTask<bool> IsCheckedAsync(WebElementReference element, CancellationToken cancellationToken = default)
        => await Task.Run(() => TryInspect(element, resolved => resolved.Selected), cancellationToken);

    public async ValueTask<bool> EvaluateBooleanAsync(string script, CancellationToken cancellationToken = default)
        => await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Driver is not IJavaScriptExecutor javascript)
                throw new WebBackendCapabilityException(
                    $"Selenium driver '{Driver.GetType().FullName}' does not support JavaScript execution.");
            return Convert.ToBoolean(javascript.ExecuteScript($"return Boolean({script});"));
        }, cancellationToken);

    public async ValueTask<IReadOnlyList<ProtoTestAttachment>> CaptureFailureAsync(
        WebFailureContext failure,
        CancellationToken cancellationToken = default)
    {
        _webFailure = true;
        return await Task.Run<IReadOnlyList<ProtoTestAttachment>>(() =>
        {
            var attachments = new List<ProtoTestAttachment>();
            var prefix = $"{SafeName(_sessionName)}-{SafeName(failure.Element?.Name ?? failure.Operation)}";
            try
            {
                if (Driver is ITakesScreenshot screenshots)
                {
                    attachments.Add(ProtoTestAttachment.FromBytes(
                        $"web-{prefix}-failure.png", screenshots.GetScreenshot().AsByteArray,
                        "image/png", "Selenium page at web operation failure."));
                }
            }
            catch (Exception exception) { RecordCaptureFailure("screenshot", exception); }
            try
            {
                attachments.Add(ProtoTestAttachment.FromText(
                    $"web-{prefix}-page.html", Driver.PageSource,
                    "text/html", "DOM snapshot at web operation failure."));
            }
            catch (Exception exception) { RecordCaptureFailure("dom", exception); }
            try
            {
                attachments.Add(ProtoTestAttachment.FromText(
                    $"web-{prefix}-location.txt", $"URL: {Driver.Url}{Environment.NewLine}Title: {Driver.Title}",
                    "text/plain", "Browser location at web operation failure."));
            }
            catch (Exception exception) { RecordCaptureFailure("location", exception); }
            return attachments;
        }, cancellationToken);
    }

    private void ExecuteActionable(
        WebElementReference reference,
        WebOperationKind operation,
        Action<IWebElement> action,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        string lastObserved = "not found";
        ElementBounds? previousBounds = null;
        var attempt = 0;
        var clickLike = operation is WebOperationKind.Click or WebOperationKind.Check;
        var editable = operation is WebOperationKind.Fill or WebOperationKind.SelectOption;
        while (stopwatch.Elapsed < _options.ActionTimeout)
        {
            attempt++;
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var scope = ResolveScope(reference);
                var element = ResolveSingle(scope, reference.Locator, reference.ComponentPath);
                if (!element.Displayed || !element.Enabled)
                {
                    lastObserved = $"displayed={element.Displayed.ToString().ToLowerInvariant()}, enabled={element.Enabled.ToString().ToLowerInvariant()}";
                    previousBounds = null;
                    Record(operation, reference, attempt, "waiting", lastObserved, stopwatch.Elapsed);
                    WaitForNextPoll(cancellationToken);
                    continue;
                }

                if (editable && IsReadOnly(element))
                {
                    lastObserved = "element is readonly";
                    Record(operation, reference, attempt, "waiting", lastObserved, stopwatch.Elapsed);
                    WaitForNextPoll(cancellationToken);
                    continue;
                }

                if (clickLike)
                    ScrollIntoView(element);

                if (clickLike && _options.WaitForStableBounds)
                {
                    var bounds = new ElementBounds(
                        element.Location.X,
                        element.Location.Y,
                        element.Size.Width,
                        element.Size.Height);
                    if (previousBounds is null || previousBounds.Value != bounds)
                    {
                        previousBounds = bounds;
                        lastObserved = "element bounds are not stable yet";
                        Record(operation, reference, attempt, "waiting", lastObserved, stopwatch.Elapsed);
                        WaitForNextPoll(cancellationToken);
                        continue;
                    }
                }

                if (clickLike && _options.CheckClickObstruction && IsObstructed(element))
                {
                    lastObserved = "another element covers the click point";
                    Record(operation, reference, attempt, "waiting", lastObserved, stopwatch.Elapsed);
                    WaitForNextPoll(cancellationToken);
                    continue;
                }

                action(element);
                Record(operation, reference, attempt, "succeeded", "action completed", stopwatch.Elapsed);
                return;
            }
            catch (NoSuchElementException)
            {
                lastObserved = "not found";
                Record(operation, reference, attempt, "retry", lastObserved, stopwatch.Elapsed);
            }
            catch (StaleElementReferenceException)
            {
                lastObserved = "stale element; resolving again";
                previousBounds = null;
                Record(operation, reference, attempt, "retry", lastObserved, stopwatch.Elapsed);
            }
            catch (ElementClickInterceptedException exception)
            {
                lastObserved = $"click intercepted: {exception.Message}";
                Record(operation, reference, attempt, "retry", lastObserved, stopwatch.Elapsed);
            }
            catch (ElementNotInteractableException exception)
            {
                lastObserved = $"not interactable: {exception.Message}";
                Record(operation, reference, attempt, "retry", lastObserved, stopwatch.Elapsed);
            }
            catch (InvalidElementStateException exception)
            {
                lastObserved = $"invalid element state: {exception.Message}";
                Record(operation, reference, attempt, "retry", lastObserved, stopwatch.Elapsed);
            }

            WaitForNextPoll(cancellationToken);
        }

        Record(operation, reference, attempt, "failed", lastObserved, stopwatch.Elapsed);
        throw new WebActionabilityException(
            $"Element '{reference.ComponentPath}.{reference.Name}' did not become actionable within {_options.ActionTimeout}. " +
            $"Locator: {reference.Locator.Describe()}. Last observed: {lastObserved}.");
    }

    private IWebElement ResolvePresent(WebElementReference reference, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < _options.ActionTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return ResolveSingle(ResolveScope(reference), reference.Locator, reference.ComponentPath);
            }
            catch (NoSuchElementException) { }
            catch (StaleElementReferenceException) { }
            WaitForNextPoll(cancellationToken);
        }
        throw new WebActionabilityException(
            $"Element '{reference.ComponentPath}.{reference.Name}' was not present within {_options.ActionTimeout}. " +
            $"Locator: {reference.Locator.Describe()}.");
    }

    private ISearchContext ResolveScope(WebElementReference reference)
    {
        ISearchContext scope = Driver;
        foreach (var root in reference.ComponentRoots)
            scope = ResolveSingle(scope, root, reference.ComponentPath);
        return scope;
    }

    private static IWebElement ResolveSingle(ISearchContext scope, WebLocator locator, string componentPath)
    {
        if (locator is NthWebLocator nth)
        {
            var indexedMatches = scope.FindElements(SeleniumLocatorTranslator.Translate(nth.Source));
            if (indexedMatches.Count <= nth.Index)
                throw new NoSuchElementException(
                    $"No element exists at zero-based index {nth.Index} for {nth.Source.Describe()} in {componentPath}; found {indexedMatches.Count}.");
            return indexedMatches[nth.Index];
        }

        var seleniumBy = SeleniumLocatorTranslator.Translate(locator);
        var matches = scope.FindElements(seleniumBy);
        return matches.Count switch
        {
            0 => throw new NoSuchElementException($"No element matched {locator.Describe()} in {componentPath}."),
            1 => matches[0],
            _ => throw new WebElementResolutionException(
                $"Expected one element for {locator.Describe()} in {componentPath}, but found {matches.Count}.")
        };
    }

    private static bool IsReadOnly(IWebElement element)
        => element.GetDomAttribute("readonly") is not null ||
           string.Equals(element.GetDomAttribute("aria-readonly"), "true", StringComparison.OrdinalIgnoreCase);

    private bool TryInspect(WebElementReference reference, Func<IWebElement, bool> inspect)
    {
        try
        {
            return inspect(ResolveSingle(ResolveScope(reference), reference.Locator, reference.ComponentPath));
        }
        catch (NoSuchElementException) { return false; }
        catch (StaleElementReferenceException) { return false; }
    }

    private bool IsObstructed(IWebElement element)
    {
        if (Driver is not IJavaScriptExecutor javascript) return false;
        try
        {
            const string script = "var e=arguments[0],r=e.getBoundingClientRect()," +
                                  "x=r.left+r.width/2,y=r.top+r.height/2,h=document.elementFromPoint(x,y);" +
                                  "return h!==e&&!e.contains(h);";
            return Convert.ToBoolean(javascript.ExecuteScript(script, element));
        }
        catch (WebDriverException)
        {
            // Older or restricted drivers may not support this diagnostic primitive.
            return false;
        }
    }

    private void ScrollIntoView(IWebElement element)
    {
        if (Driver is not IJavaScriptExecutor javascript) return;
        try
        {
            javascript.ExecuteScript(
                "arguments[0].scrollIntoView({block:'center',inline:'center',behavior:'instant'});",
                element);
        }
        catch (WebDriverException)
        {
            // Selenium's native click still gets a chance to scroll on older drivers.
        }
    }

    private void WaitForNextPoll(CancellationToken cancellationToken)
    {
        cancellationToken.WaitHandle.WaitOne(_options.PollInterval);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private readonly record struct ElementBounds(int X, int Y, int Width, int Height);

    private static string MapKey(WebKey key) => key switch
    {
        WebKey.Enter => Keys.Enter,
        WebKey.Tab => Keys.Tab,
        WebKey.Escape => Keys.Escape,
        WebKey.Space => Keys.Space,
        WebKey.Backspace => Keys.Backspace,
        WebKey.Delete => Keys.Delete,
        WebKey.ArrowUp => Keys.ArrowUp,
        WebKey.ArrowDown => Keys.ArrowDown,
        WebKey.ArrowLeft => Keys.ArrowLeft,
        WebKey.ArrowRight => Keys.ArrowRight,
        WebKey.Home => Keys.Home,
        WebKey.End => Keys.End,
        WebKey.PageUp => Keys.PageUp,
        WebKey.PageDown => Keys.PageDown,
        _ => throw new ArgumentOutOfRangeException(nameof(key))
    };

    private void Record(
        WebOperationKind operation,
        WebElementReference reference,
        int attempt,
        string outcome,
        string observation,
        TimeSpan elapsed)
        => _diagnostics.Enqueue(new SeleniumDiagnosticEntry(
            DateTimeOffset.UtcNow,
            operation.ToString(),
            reference.ComponentPath,
            reference.Name,
            reference.Locator.Describe(),
            attempt,
            outcome,
            observation,
            elapsed.TotalMilliseconds));

    private static ValueTask Background(Action action, CancellationToken cancellationToken)
        => new(Task.Run(action, cancellationToken));

    private static string SafeName(string value) => string.Concat(value.Select(character => char.IsLetterOrDigit(character) ? character : '-')).Trim('-').ToLowerInvariant();

    private void RecordCaptureFailure(string artifact, Exception exception)
        => _context.Trace.WriteEvent(
            "web.diagnostics.artifact_failed",
            $"Selenium diagnostic failed · {artifact}",
            "ProtoTest.Web.Selenium",
            outcome: ProtoTraceOutcome.Failed,
            attributes: new Dictionary<string, string?> { ["web.artifact"] = artifact },
            exception: exception);

    public ValueTask CompleteAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _completeStarted, 1) != 0) return ValueTask.CompletedTask;
        cancellationToken.ThrowIfCancellationRequested();
        var retain = _options.DiagnosticTraceRetention == SeleniumDiagnosticTraceRetention.Always ||
                     (_options.DiagnosticTraceRetention == SeleniumDiagnosticTraceRetention.OnWebFailure && _webFailure);
        if (retain)
        {
            string? url = null;
            string? title = null;
            try
            {
                url = Driver.Url;
                title = Driver.Title;
            }
            catch (WebDriverException) { }

            var payload = new
            {
                format = "prototest.selenium.diagnostics.v1",
                startedAtUtc = _startedAtUtc,
                completedAtUtc = DateTimeOffset.UtcNow,
                driverType = Driver.GetType().FullName,
                url,
                title,
                entries = _diagnostics.ToArray()
            };
            _context.AddAttachment(
                $"selenium-{SafeName(_sessionName)}-diagnostics.json",
                JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }),
                "application/json",
                "Selenium backend diagnostic timeline embedded in ProtoTrace.");
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0) return ValueTask.CompletedTask;
        Exception? failure = null;
        try { Driver.Quit(); } catch (Exception exception) { failure = exception; }
        try { Driver.Dispose(); } catch (Exception exception) { failure ??= exception; }
        if (failure is not null) throw failure;
        return ValueTask.CompletedTask;
    }

    private sealed record SeleniumDiagnosticEntry(
        DateTimeOffset TimestampUtc,
        string Operation,
        string ComponentPath,
        string Element,
        string Locator,
        int Attempt,
        string Outcome,
        string Observation,
        double ElapsedMilliseconds);
}

internal sealed class SeleniumWebBackendFactory(
    Func<IWebDriver> createDriver,
    WebBackendOptionsBinder<SeleniumWebOptions> options,
    string sessionName) : IWebBackendFactory
{
    public string Name => SeleniumWebOptions.BackendName;
    public ValueTask<IWebBackend> CreateAsync(ProtoExecutionContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolved = options.Resolve(context.Configuration);
        return ValueTask.FromResult<IWebBackend>(new SeleniumWebBackend(context, createDriver(), resolved, sessionName));
    }
}
