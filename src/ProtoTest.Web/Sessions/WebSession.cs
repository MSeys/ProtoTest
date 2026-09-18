namespace ProtoTest.Web;

using ProtoTest.Web.Internal;

using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

/// <summary>Test-scoped entry point for pages, operations, and explicit native backend access.</summary>
public sealed class WebSession : IAsyncDisposable
{
    private const string TraceSource = "ProtoTest.Web";
    private readonly ProtoExecutionContext _context;
    private readonly IWebBackendFactory _factory;
    private readonly ProtoLock _backendGate = new();
    private readonly ProtoLock _pageGate = new();
    private readonly Dictionary<Type, WebPage> _pages = [];
    private Task<IWebBackend>? _backendTask;
    private int _completeStarted;
    private int _disposeStarted;

    internal WebSession(
        ProtoExecutionContext context,
        IWebBackendFactory factory,
        string name,
        string? application = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Application = application
            ?? ProtoApplication.ResolveName(context.Configuration, $"ProtoTest:Web:Sessions:{name}", name);
        BaseUrl = ResolveBaseUrl(context, name, Application);
    }

    public string Name { get; }

    /// <summary>
    /// Gets the application this session targets, from <c>ProtoTest:Web:Sessions:{name}:Application</c>
    /// and defaulting to the session name.
    /// </summary>
    public string Application { get; }

    /// <summary>
    /// Gets the origin for relative navigation, read from the application's
    /// <c>ProtoTest:Applications:{application}:BaseUrl</c> — the same section REST and GraphQL clients
    /// target, so a browser session and an HTTP client can share one application address.
    /// </summary>
    public Uri? BaseUrl { get; }
    public string BackendName => _backendTask is { IsCompletedSuccessfully: true }
        ? _backendTask.Result.Name
        : _factory.Name;

    /// <summary>Returns the page for this session, creating it on first use and reusing it afterwards.</summary>
    public TPage Page<TPage>() where TPage : WebPage, new()
    {
        lock (_pageGate)
        {
            if (_pages.TryGetValue(typeof(TPage), out var existing))
            {
                return (TPage)existing;
            }

            var page = new TPage();
            page.Initialize(this, new ComponentScope([], typeof(TPage).Name));
            _pages[typeof(TPage)] = page;
            return page;
        }
    }

    /// <summary>Explicit escape hatch. Referencing an adapter type makes backend coupling visible.</summary>
    public TBackend GetBackend<TBackend>() where TBackend : class, IWebBackend
        => _backendTask is { IsCompletedSuccessfully: true } && _backendTask.Result is TBackend backend
            ? backend
            : throw new InvalidOperationException(
                $"Web session '{Name}' has not initialized backend '{typeof(TBackend).Name}'. " +
                $"Use GetBackendAsync<{typeof(TBackend).Name}>() for a lazy session.");

    /// <summary>Initializes the named session when necessary and returns its native backend.</summary>
    public async ValueTask<TBackend> GetBackendAsync<TBackend>(CancellationToken cancellationToken = default)
        where TBackend : class, IWebBackend
    {
        var backend = await GetOrCreateBackendAsync(cancellationToken);
        return backend as TBackend ?? throw new WebBackendCapabilityException(
            $"The active web backend is '{backend.Name}', not '{typeof(TBackend).FullName}'.");
    }

    internal ValueTask NavigateAsync(Uri address, CancellationToken cancellationToken)
    {
        var target = ResolveTarget(address);
        return ExecuteVoidAsync(
            "web.navigate",
            $"WEB · Navigate · {target}",
            WebOperationKind.Navigate,
            null,
            new Dictionary<string, string?> { ["web.address"] = target.ToString() },
            (backend, ct) => backend.NavigateAsync(target, ct),
            cancellationToken);
    }

    private Uri ResolveTarget(Uri address)
    {
        if (address.IsAbsoluteUri)
        {
            return address;
        }

        var baseUrl = BaseUrl ?? throw new InvalidOperationException(
            $"Web session '{Name}' was asked to open the relative address '{address}', but application " +
            $"'{Application}' has no base address. Set 'ProtoTest:Applications:{Application}:BaseUrl' or " +
            $"'ProtoTest:Web:Sessions:{Name}:BaseUrl'.");
        return new Uri(baseUrl, address);
    }

    private static Uri? ResolveBaseUrl(ProtoExecutionContext context, string sessionName, string applicationName)
    {
        // A session can target its own address (for example a standalone instance infrastructure started,
        // which fills it after the host started); otherwise it shares the application's address with the
        // HTTP-based protocols.
        string? configured = null;
        var sessionKey = $"ProtoTest:Web:Sessions:{sessionName}:BaseUrl";
        if (context.TryService<ProtoInfrastructureSettings>() is { } settings
            && settings.Values.TryGetValue(sessionKey, out var provided))
        {
            configured = provided;
        }

        configured ??= context.Configuration[sessionKey];
        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = ProtoApplication.BaseUrl(context.Configuration, applicationName);
        }

        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        return Uri.TryCreate(configured, UriKind.Absolute, out var uri)
            ? uri
            : throw new InvalidOperationException(
                $"The base URL '{configured}' for web session '{sessionName}' must be an absolute URI.");
    }

    internal ValueTask ClickAsync(WebElementReference element, CancellationToken cancellationToken)
        => ExecuteVoidAsync(
            "web.click",
            $"WEB · Click · {element.Name}",
            WebOperationKind.Click,
            element,
            ElementAttributes(element),
            (backend, ct) => backend.ClickAsync(element, ct),
            cancellationToken);

    internal ValueTask FillAsync(WebElementReference element, string value, CancellationToken cancellationToken)
    {
        var attributes = ElementAttributes(element);
        attributes["web.value"] = "[REDACTED]";
        attributes["web.value.length"] = value.Length.ToString();
        return ExecuteVoidAsync(
            "web.fill",
            $"WEB · Fill · {element.Name}",
            WebOperationKind.Fill,
            element,
            attributes,
            (backend, ct) => backend.FillAsync(element, value, ct),
            cancellationToken);
    }

    internal ValueTask CheckAsync(WebElementReference element, bool isChecked, CancellationToken cancellationToken)
        => ExecuteVoidAsync(
            "web.check",
            $"WEB · {(isChecked ? "Check" : "Uncheck")} · {element.Name}",
            WebOperationKind.Check,
            element,
            ElementAttributes(element),
            (backend, ct) => backend.CheckAsync(element, isChecked, ct),
            cancellationToken);

    internal ValueTask SelectOptionAsync(WebElementReference element, string value, CancellationToken cancellationToken)
    {
        var attributes = ElementAttributes(element);
        attributes["web.option"] = value;
        return ExecuteVoidAsync(
            "web.select_option",
            $"WEB · Select option · {element.Name}",
            WebOperationKind.SelectOption,
            element,
            attributes,
            (backend, ct) => backend.SelectOptionAsync(element, value, ct),
            cancellationToken);
    }

    internal ValueTask PressAsync(WebElementReference element, WebKey key, CancellationToken cancellationToken)
    {
        var attributes = ElementAttributes(element);
        attributes["web.key"] = key.ToString();
        return ExecuteVoidAsync(
            "web.press",
            $"WEB · Press {key} · {element.Name}",
            WebOperationKind.Press,
            element,
            attributes,
            (backend, ct) => backend.PressAsync(element, key, ct),
            cancellationToken);
    }

    internal ValueTask<int> CountAsync(WebElementReference elements, CancellationToken cancellationToken)
        => ExecuteAsync(
            "web.count",
            $"WEB · Count · {elements.Name}",
            WebOperationKind.Count,
            elements,
            ElementAttributes(elements),
            (backend, ct) => backend.CountAsync(elements, ct),
            cancellationToken);

    internal ValueTask<string> ReadTextAsync(WebElementReference element, CancellationToken cancellationToken)
        => ExecuteAsync(
            "web.read_text",
            $"WEB · Read text · {element.Name}",
            WebOperationKind.ReadText,
            element,
            ElementAttributes(element),
            (backend, ct) => backend.ReadTextAsync(element, ct),
            cancellationToken);

    internal ValueTask<string?> ReadValueAsync(WebElementReference element, CancellationToken cancellationToken)
        => ExecuteAsync(
            "web.read_value",
            $"WEB · Read value · {element.Name}",
            WebOperationKind.ReadValue,
            element,
            ElementAttributes(element),
            (backend, ct) => backend.ReadValueAsync(element, ct),
            cancellationToken);

    internal ValueTask<bool> IsVisibleAsync(WebElementReference element, CancellationToken cancellationToken)
        => ExecuteAsync(
            "web.is_visible",
            $"WEB · Is visible · {element.Name}",
            WebOperationKind.IsVisible,
            element,
            ElementAttributes(element),
            (backend, ct) => backend.IsVisibleAsync(element, ct),
            cancellationToken);

    internal ValueTask<bool> IsEnabledAsync(WebElementReference element, CancellationToken cancellationToken)
        => ExecuteAsync(
            "web.is_enabled",
            $"WEB · Is enabled · {element.Name}",
            WebOperationKind.IsEnabled,
            element,
            ElementAttributes(element),
            (backend, ct) => backend.IsEnabledAsync(element, ct),
            cancellationToken);

    internal ValueTask<bool> IsCheckedAsync(WebElementReference element, CancellationToken cancellationToken)
        => ExecuteAsync(
            "web.is_checked",
            $"WEB · Is checked · {element.Name}",
            WebOperationKind.IsChecked,
            element,
            ElementAttributes(element),
            (backend, ct) => backend.IsCheckedAsync(element, ct),
            cancellationToken);

    internal ValueTask ShouldBeVisibleAsync(
        WebElementReference element,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
        => AssertUntilAsync(
            element,
            "be visible",
            timeout,
            async (backend, ct) => await backend.IsVisibleAsync(element, ct)
                ? (true, "visible")
                : (false, "not visible"),
            cancellationToken);

    internal ValueTask ShouldBeEnabledAsync(
        WebElementReference element,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
        => AssertUntilAsync(
            element,
            "be enabled",
            timeout,
            async (backend, ct) => await backend.IsEnabledAsync(element, ct)
                ? (true, "enabled")
                : (false, "disabled or absent"),
            cancellationToken);

    internal ValueTask ShouldBeCheckedAsync(
        WebElementReference element,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
        => AssertUntilAsync(
            element,
            "be checked",
            timeout,
            async (backend, ct) => await backend.IsCheckedAsync(element, ct)
                ? (true, "checked")
                : (false, "unchecked or absent"),
            cancellationToken);

    internal ValueTask ShouldHaveTextAsync(
        WebElementReference element,
        string expected,
        bool contains,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
        => AssertUntilAsync(
            element,
            contains ? $"contain text \"{expected}\"" : $"have text \"{expected}\"",
            timeout,
            async (backend, ct) =>
            {
                var actual = await backend.ReadTextAsync(element, ct);
                var matches = contains
                    ? actual.Contains(expected, StringComparison.Ordinal)
                    : string.Equals(actual, expected, StringComparison.Ordinal);
                return (matches, $"text was \"{actual}\"");
            },
            cancellationToken);

    internal ValueTask ShouldHaveValueAsync(
        WebElementReference element,
        string expected,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
        => AssertUntilAsync(
            element,
            "have the expected value",
            timeout,
            async (backend, ct) =>
            {
                var actual = await backend.ReadValueAsync(element, ct);
                return (string.Equals(actual, expected, StringComparison.Ordinal),
                    $"value length was {actual?.Length ?? 0} (value redacted)");
            },
            cancellationToken);

    /// <summary>
    /// Polls <paramref name="condition"/> until it holds or <paramref name="timeout"/> elapses. Use it to
    /// wait on one session for a change another session produced, or for any condition that is not a
    /// single element assertion.
    /// </summary>
    /// <param name="condition">Returns whether the awaited condition currently holds.</param>
    /// <param name="timeout">How long to keep polling. Defaults to five seconds.</param>
    /// <param name="description">Defaults to the source text of <paramref name="condition"/>.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    public ValueTask WaitUntilAsync(
        Func<CancellationToken, ValueTask<bool>> condition,
        TimeSpan? timeout = null,
        [CallerArgumentExpression(nameof(condition))] string? description = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(condition);
        var waitTimeout = timeout ?? TimeSpan.FromSeconds(5);
        if (waitTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        var expectation = string.IsNullOrWhiteSpace(description) ? "the condition to hold" : description;

        return ExecuteVoidAsync(
            "web.wait.until",
            $"Wait · {Shorten(expectation)}",
            WebOperationKind.Assert,
            element: null,
            new Dictionary<string, string?>
            {
                ["web.session"] = Name,
                ["web.expectation"] = expectation,
                ["web.wait.timeout"] = waitTimeout.ToString()
            },
            async (_, ct) =>
            {
                var result = await WebPolling.PollAsync(
                    async token =>
                    {
                        try
                        {
                            return await condition(token);
                        }
                        catch (WebElementResolutionException)
                        {
                            return false;
                        }
                        catch (WebActionabilityException)
                        {
                            return false;
                        }
                    },
                    satisfied => satisfied,
                    waitTimeout,
                    WebPolling.DefaultInterval,
                    ct);

                if (!result.Satisfied)
                {
                    throw new WebAssertionException(
                        $"Expected {expectation} within {waitTimeout}, but it did not hold.");
                }
            },
            cancellationToken);
    }

    private static string Shorten(string value)
        => value.Length <= 80 ? value : $"{value[..77]}...";

    private ValueTask AssertUntilAsync(
        WebElementReference element,
        string expectation,
        TimeSpan? timeout,
        Func<IWebBackend, CancellationToken, ValueTask<(bool Matches, string Observation)>> inspect,
        CancellationToken cancellationToken)
    {
        var assertionTimeout = timeout ?? TimeSpan.FromSeconds(5);
        if (assertionTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        var attributes = ElementAttributes(element);
        attributes["web.expectation"] = expectation;
        attributes["web.assert.timeout"] = assertionTimeout.ToString();
        return ExecuteVoidAsync(
            "assert.web",
            $"Assert · {element.Name} should {expectation}",
            WebOperationKind.Assert,
            element,
            attributes,
            async (backend, ct) =>
            {
                var result = await WebPolling.PollAsync(
                    async token =>
                    {
                        try
                        {
                            return await inspect(backend, token);
                        }
                        catch (WebElementResolutionException exception)
                        {
                            return (Matches: false, Observation: exception.Message);
                        }
                        catch (WebActionabilityException exception)
                        {
                            return (Matches: false, Observation: exception.Message);
                        }
                    },
                    observation => observation.Matches,
                    assertionTimeout,
                    WebPolling.DefaultInterval,
                    ct);

                if (!result.Satisfied)
                {
                    throw new WebAssertionException(
                        $"Element '{element.ComponentPath}.{element.Name}' should {expectation} within {assertionTimeout}. " +
                        $"Last observed: {result.Value.Observation ?? "no observation"}.");
                }
            },
            cancellationToken);
    }

    private async ValueTask ExecuteVoidAsync(
        string kind,
        string name,
        WebOperationKind operationKind,
        WebElementReference? element,
        Dictionary<string, string?> attributes,
        Func<IWebBackend, CancellationToken, ValueTask> execute,
        CancellationToken cancellationToken)
        => await ExecuteAsync<object?>(
            kind,
            name,
            operationKind,
            element,
            attributes,
            async (backend, ct) =>
            {
                await execute(backend, ct);
                return null;
            },
            cancellationToken);

    private async ValueTask<TResult> ExecuteAsync<TResult>(
        string kind,
        string name,
        WebOperationKind operationKind,
        WebElementReference? element,
        Dictionary<string, string?> attributes,
        Func<IWebBackend, CancellationToken, ValueTask<TResult>> execute,
        CancellationToken cancellationToken)
    {
        var backend = await GetOrCreateBackendAsync(cancellationToken);
        attributes["web.backend"] = backend.Name;
        attributes["web.session"] = Name;
        using var operation = _context.Trace
            .Operation(kind, name, TraceSource)
            .With(attributes)
            .Begin();
        var backendContext = new WebBackendOperationContext(
            operation.Id, Name, operationKind, OperationName(name), element);
        var webOperation = new WebOperationContext(
            _context, operationKind, OperationName(name), backend.Name, Name, operation.Id, element, backend);
        try
        {
            WebOperationDelegate pipeline = async (context, ct) =>
            {
                using var backendOperation = _context.Trace
                    .Operation("web.backend.execute", $"{backend.Name} · {operationKind}", TraceSource)
                    .With("web.backend", backend.Name)
                    .With("web.session", Name)
                    .With("web.correlation_id", operation.Id)
                    .Parent(operation.Id)
                    .Begin();
                Exception? failure = null;
                var outcome = ProtoTraceOutcome.Unknown;
                try
                {
                    await backend.BeginOperationAsync(backendContext, ct);
                    context.Result = await execute(backend, ct);
                    outcome = ProtoTraceOutcome.Succeeded;
                    backendOperation.Succeed();
                }
                catch (OperationCanceledException exception)
                {
                    failure = exception;
                    outcome = ProtoTraceOutcome.Cancelled;
                    backendOperation.Cancel(exception);
                    throw;
                }
                catch (Exception exception)
                {
                    failure = exception;
                    outcome = ProtoTraceOutcome.Failed;
                    backendOperation.Fail(exception);
                    throw;
                }
                finally
                {
                    await backend.EndOperationAsync(backendContext, outcome, failure, CancellationToken.None);
                }
            };

            foreach (var middleware in _context.Services.GetServices<IWebOperationMiddleware>().Reverse())
            {
                var next = pipeline;
                pipeline = (context, ct) => middleware.InvokeAsync(context, next, ct);
            }

            await pipeline(webOperation, cancellationToken);

            operation.Succeed();
            return (TResult)webOperation.Result!;
        }
        catch (OperationCanceledException exception)
        {
            operation.Cancel(exception);
            throw;
        }
        catch (Exception exception)
        {
            await CaptureFailureAsync(backend, new WebFailureContext(operationKind.ToString(), element, exception), operation.Id);
            operation.Fail(exception);
            throw;
        }
    }

    private static string OperationName(string traceName)
        => traceName.StartsWith("WEB · ", StringComparison.Ordinal) ? traceName[6..] : traceName;

    private async ValueTask CaptureFailureAsync(IWebBackend backend, WebFailureContext failure, string parentId)
    {
        if (backend is not IWebBackendDiagnostics diagnostics)
        {
            return;
        }

        try
        {
            var attachments = await diagnostics.CaptureFailureAsync(failure);
            foreach (var attachment in attachments)
                _context.AddAttachment(attachment);
        }
        catch (Exception captureException)
        {
            _context.Trace.WriteEvent(
                "web.diagnostics.failed",
                "Web diagnostics capture failed",
                TraceSource,
                outcome: ProtoTraceOutcome.Failed,
                exception: captureException,
                parentId: parentId);
        }
    }

    private static Dictionary<string, string?> ElementAttributes(WebElementReference element)
        => new()
        {
            ["web.component"] = element.ComponentPath,
            ["web.element"] = element.Name,
            ["web.locator"] = element.Locator.Describe(),
            ["web.component.roots"] = string.Join(" > ", element.ComponentRoots.Select(root => root.Describe()))
        };

    internal async ValueTask RunFlowAsync(
        string name,
        IReadOnlyList<Func<CancellationToken, ValueTask>> steps,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        using var operation = _context.Trace
            .Operation("web.flow", $"WEB flow · {name}", TraceSource)
            .With("web.session", Name)
            .With("web.backend", BackendName)
            .With("web.flow.step_count", steps.Count.ToString())
            .Begin();
        try
        {
            foreach (var step in steps)
                await step(cancellationToken);
            operation.Succeed();
        }
        catch (OperationCanceledException exception)
        {
            operation.Cancel(exception);
            throw;
        }
        catch (Exception exception)
        {
            operation.Fail(exception);
            throw;
        }
    }

    private Task<IWebBackend> GetOrCreateBackendAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_backendGate)
        {
            return _backendTask ??= CreateBackendAsync(cancellationToken);
        }
    }

    private async Task<IWebBackend> CreateBackendAsync(CancellationToken cancellationToken)
    {
        using var operation = _context.Trace
            .Operation("web.session.initialize", $"Initialize web session · {Name}", TraceSource)
            .With("web.session", Name)
            .With("web.backend", _factory.Name)
            .Begin();
        try
        {
            var backend = await _factory.CreateAsync(_context, Name, cancellationToken);
            operation.SetAttribute("web.backend", backend.Name);
            operation.Succeed();
            return backend;
        }
        catch (Exception exception)
        {
            operation.Fail(exception);
            lock (_backendGate) _backendTask = null;
            throw;
        }
    }

    internal async ValueTask CompleteAsync()
    {
        if (Interlocked.Exchange(ref _completeStarted, 1) != 0) return;
        var backendTask = _backendTask;
        if (backendTask is null) return;
        var backend = await backendTask;
        using var operation = _context.Trace
            .Operation("web.session.complete", $"Complete web session · {Name}", TraceSource)
            .During(ProtoTracePhase.Teardown)
            .With("web.backend", backend.Name)
            .With("web.session", Name)
            .Begin();
        try
        {
            await backend.CompleteAsync();
            operation.Succeed();
        }
        catch (Exception exception)
        {
            operation.Fail(exception);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0) return;
        await CompleteAsync();
        if (_backendTask is { } backendTask)
            await (await backendTask).DisposeAsync();
    }
}
