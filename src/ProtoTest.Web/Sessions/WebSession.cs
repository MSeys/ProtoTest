namespace ProtoTest.Web;

using System.Runtime.CompilerServices;
using ProtoTest.Core;
using ProtoTest.Web.Internal;
using ProtoTest.Web.Pages;

/// <summary>Test-scoped entry point for pages, operations, and explicit native backend access.</summary>
public sealed class WebSession : IAsyncDisposable, IProtoClientCompletion
{
    internal const string TraceSource = "ProtoTest.Web";
    private readonly ProtoExecutionContext _context;
    private readonly IWebBackendFactory _factory;
    private readonly ProtoLock _backendGate = new();
    private readonly ProtoLock _pageGate = new();
    private readonly Dictionary<Type, WebPage> _pages = [];
    private readonly WebOperationRunner _operations;
    private readonly WebAssertionPoller _assertions;
    private readonly WebDownloadCapture _downloads;
    private readonly WebRouteDiscovery _routeDiscovery;
    private Task<IWebBackend>? _backendTask;
    private int _completeStarted;
    private int _disposeStarted;

    internal WebSession(
        ProtoExecutionContext context,
        IWebBackendFactory factory,
        string name,
        string? application = null,
        string? endpoint = null,
        bool discoverRoutes = false)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Endpoint = endpoint;
        DiscoverRoutes = discoverRoutes;
        Application = string.IsNullOrWhiteSpace(application) ? name : application;
        BaseUrl = ResolveBaseUrl(context, Application, endpoint);
        _operations = new WebOperationRunner(
            context,
            Name,
            TraceSource,
            cancellationToken => new ValueTask<IWebBackend>(GetOrCreateBackendAsync(cancellationToken)));
        _assertions = new WebAssertionPoller(this, _operations, new WebProbeLoop(BackendPollInterval));
        _downloads = new WebDownloadCapture(context, Name, TraceSource, _operations);
        _routeDiscovery = new WebRouteDiscovery(context, Name, TraceSource, discoverRoutes);
    }

    public string Name { get; }

    /// <summary>
    /// Gets the application this session targets: the one the test selected, or the session name. Its
    /// address comes from <c>ProtoTest:Applications:{application}:BaseUrl</c>, the same setting REST,
    /// GraphQL and gRPC resolve, so a browser session and an HTTP client share one application address.
    /// </summary>
    public string Application { get; }

    /// <summary>Gets the application endpoint this session is rooted at, or <see langword="null"/>.</summary>
    public string? Endpoint { get; }

    /// <summary>Gets whether Vue Router route discovery is enabled for this session.</summary>
    public bool DiscoverRoutes { get; }

    /// <summary>
    /// Gets the origin for relative navigation, read from the application's base address (optionally
    /// joined with the named endpoint) — the same setting the HTTP-based protocols target.
    /// </summary>
    public Uri? BaseUrl { get; }
    public string BackendName => _backendTask is { IsCompletedSuccessfully: true }
        ? _backendTask.Result.Name
        : _factory.Name;

    /// <summary>The polling engine behind element assertions and <c>WaitUntilAsync</c>.</summary>
    internal WebAssertionPoller Assertions => _assertions;

    // The session's one wait timing: the backend's configured interval when it has one, the shared
    // default otherwise. Resolved per poll, because the backend is created lazily on first use.
    private TimeSpan BackendPollInterval()
        => _backendTask is { IsCompletedSuccessfully: true }
            ? _backendTask.Result.PollInterval
            : WebTiming.DefaultPollInterval;

    /// <summary>Returns the page for this session, creating it on first use and reusing it afterwards.</summary>
    public TPage Page<TPage>() where TPage : WebPage, new()
    {
        lock (_pageGate)
        {
            if (_pages.TryGetValue(typeof(TPage), out var existing))
            {
                return (TPage)existing;
            }

            var page = new ComponentScope([], typeof(TPage).Name).Create<TPage>(this);
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

    internal async ValueTask NavigateAsync(Uri address, CancellationToken cancellationToken)
    {
        var target = ResolveTarget(address);
        var safeAddress = ProtoUriSanitizer.Sanitize(target);
        await _operations.ExecuteVoidAsync(
            "web.navigate",
            $"WEB · Navigate · {safeAddress}",
            WebOperationKind.Navigate,
            null,
            new Dictionary<string, string?> { ["web.address"] = safeAddress },
            (backend, ct) => backend.NavigateAsync(target, ct),
            cancellationToken);

        var backend = await GetOrCreateBackendAsync(cancellationToken);
        // A redirect lands on a different page, so the backend's final address wins over the target. The
        // target is only the fallback when the backend cannot report an address at all; a reported
        // external origin is deliberately not replaced by the target.
        var currentAddress = await TryCurrentAddressAsync(backend, cancellationToken);
        RecordPageObservation(
            WebPageInventory.VisitedObservationKind,
            currentAddress is null ? PagePathFrom(target.ToString()) : PagePathFrom(currentAddress),
            "navigate");
        await _routeDiscovery.DiscoverAsync(backend, cancellationToken);
    }

    private Uri ResolveTarget(Uri address)
    {
        if (address.IsAbsoluteUri)
        {
            return address;
        }

        var baseUrl = BaseUrl ?? throw new InvalidOperationException(
            $"Web session '{Name}' was asked to open the relative address '{address}', but application " +
            $"'{Application}' has no base address. Set 'ProtoTest:Applications:{Application}:BaseUrl'.");
        return new Uri(baseUrl, address);
    }

    private static Uri? ResolveBaseUrl(ProtoExecutionContext context, string application, string? endpoint)
    {
        // Sessions follow the application address like every other client; a started standalone
        // instance advertises that same setting through infrastructure settings.
        var address = ProtoApplication.EndpointAddress(context, application, endpoint);
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        return Uri.TryCreate(address, UriKind.Absolute, out var uri)
            ? uri
            : throw new InvalidOperationException(
                $"The base URL '{address}' for web session application '{application}' must be an absolute URI. " +
                $"Set 'ProtoTest:Applications:{application}:BaseUrl'.");
    }

    internal ValueTask ClickAsync(WebElementReference element, CancellationToken cancellationToken)
        => _operations.ExecuteVoidAsync(
            WebOperations.Click,
            element,
            ElementAttributes(element),
            (backend, ct) => backend.ClickAsync(element, ct),
            cancellationToken);

    internal ValueTask FillAsync(WebElementReference element, string value, CancellationToken cancellationToken)
    {
        var attributes = ElementAttributes(element);
        attributes["web.value"] = "[REDACTED]";
        attributes["web.value.length"] = value.Length.ToString();
        return _operations.ExecuteVoidAsync(
            WebOperations.Fill,
            element,
            attributes,
            (backend, ct) => backend.FillAsync(element, value, ct),
            cancellationToken);
    }

    internal ValueTask CheckAsync(WebElementReference element, bool isChecked, CancellationToken cancellationToken)
        => _operations.ExecuteVoidAsync(
            isChecked ? WebOperations.Check : WebOperations.Uncheck,
            element,
            ElementAttributes(element),
            (backend, ct) => backend.CheckAsync(element, isChecked, ct),
            cancellationToken);

    internal ValueTask SelectOptionAsync(WebElementReference element, string value, CancellationToken cancellationToken)
    {
        var attributes = ElementAttributes(element);
        attributes["web.option"] = value;
        return _operations.ExecuteVoidAsync(
            WebOperations.SelectOption,
            element,
            attributes,
            (backend, ct) => backend.SelectOptionAsync(element, value, ct),
            cancellationToken);
    }

    internal ValueTask PressAsync(WebElementReference element, WebKey key, CancellationToken cancellationToken)
    {
        var attributes = ElementAttributes(element);
        attributes["web.key"] = key.ToString();
        return _operations.ExecuteVoidAsync(
            WebOperations.Press,
            element,
            attributes,
            (backend, ct) => backend.PressAsync(element, key, ct),
            cancellationToken,
            detail: key.ToString());
    }

    internal ValueTask<int> CountAsync(WebElementReference elements, CancellationToken cancellationToken)
        => _operations.ExecuteAsync(
            WebOperations.Count,
            elements,
            ElementAttributes(elements),
            (backend, ct) => backend.CountAsync(elements, ct),
            cancellationToken);

    internal ValueTask<string> ReadTextAsync(WebElementReference element, CancellationToken cancellationToken)
        => _operations.ExecuteAsync(
            WebOperations.ReadText,
            element,
            ElementAttributes(element),
            (backend, ct) => backend.ReadTextAsync(element, ct),
            cancellationToken);

    internal ValueTask<string?> ReadValueAsync(WebElementReference element, CancellationToken cancellationToken)
        => _operations.ExecuteAsync(
            WebOperations.ReadValue,
            element,
            ElementAttributes(element),
            (backend, ct) => backend.ReadValueAsync(element, ct),
            cancellationToken);

    internal ValueTask<bool> IsVisibleAsync(WebElementReference element, CancellationToken cancellationToken)
        => _operations.ExecuteAsync(
            WebOperations.IsVisible,
            element,
            ElementAttributes(element),
            (backend, ct) => backend.IsVisibleAsync(element, ct),
            cancellationToken);

    internal ValueTask<bool> IsEnabledAsync(WebElementReference element, CancellationToken cancellationToken)
        => _operations.ExecuteAsync(
            WebOperations.IsEnabled,
            element,
            ElementAttributes(element),
            (backend, ct) => backend.IsEnabledAsync(element, ct),
            cancellationToken);

    internal ValueTask<bool> IsCheckedAsync(WebElementReference element, CancellationToken cancellationToken)
        => _operations.ExecuteAsync(
            WebOperations.IsChecked,
            element,
            ElementAttributes(element),
            (backend, ct) => backend.IsCheckedAsync(element, ct),
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
        => _assertions.WaitUntilAsync(condition, timeout, description, cancellationToken);

    /// <summary>
    /// Runs <paramref name="trigger"/> — typically a click that starts a download — and captures the
    /// file the browser downloads. The file is returned and registered as a test attachment; a backend
    /// without the download capability throws <see cref="WebBackendCapabilityException"/> before the
    /// trigger runs.
    /// </summary>
    /// <param name="trigger">The action that starts the download.</param>
    /// <param name="name">
    /// An explicit file name for the capture. Defaults to the browser's suggested file name; an
    /// extension in the explicit name refines the media type guess.
    /// </param>
    /// <param name="timeout">
    /// How long the backend waits for the download. When omitted, the backend's own wait timeout applies.
    /// </param>
    /// <param name="cancellationToken">Cancels the capture.</param>
    public ValueTask<WebDownload> DownloadAsync(
        Func<CancellationToken, Task> trigger,
        string? name = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        => _downloads.DownloadAsync(trigger, name, timeout, cancellationToken);

    /// <summary>
    /// The coverage path of an observed address. When the session targets an application (it has a
    /// <see cref="BaseUrl"/>), a page on another origin — an identity provider, a payment gateway — is
    /// not this application's page, so it contributes nothing to its coverage.
    /// </summary>
    internal string? PagePathFrom(string? address)
    {
        if (address is null) return null;
        if (BaseUrl is null || !Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            return WebPagePath.FromAddress(address);
        }

        if (!uri.IsAbsoluteUri || !IsSameOrigin(BaseUrl, uri)) return null;
        return WebPagePath.FromUri(uri);
    }

    private static bool IsSameOrigin(Uri left, Uri right)
        => string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.IdnHost, right.IdnHost, StringComparison.OrdinalIgnoreCase)
           && left.Port == right.Port;

    internal static Dictionary<string, string?> ElementAttributes(WebElementReference element)
        => new()
        {
            ["web.component"] = element.ComponentPath,
            ["web.element"] = element.Name,
            ["web.locator"] = element.Locator.Describe(),
            ["web.component.roots"] = string.Join(" > ", element.ComponentRoots.Select(root => root.Describe()))
        };

    internal void RecordPageObservation(string kind, string? path, string source)
    {
        if (path is null) return;
        _context.RecordObservation(new ProtoObservation(
            "Web",
            kind,
            path,
            Metadata: new Dictionary<string, object>
            {
                ["web.session"] = Name,
                [WebPageInventory.SourceMetadataKey] = source
            }));
    }

    internal static async ValueTask<string?> TryCurrentAddressAsync(
        IWebBackend backend,
        CancellationToken cancellationToken)
    {
        try
        {
            return await backend.GetCurrentAddressAsync(cancellationToken);
        }
        catch (Exception)
        {
            // Reading the address is best-effort; a backend that cannot report one still passes the test.
            return null;
        }
    }

    internal async ValueTask RunFlowAsync(
        string name,
        IReadOnlyList<Func<CancellationToken, ValueTask>> steps,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await _context.Trace
            .Operation("web.flow", $"WEB flow · {name}", TraceSource)
            .With("web.session", Name)
            .With("web.backend", BackendName)
            .With("web.flow.step_count", steps.Count.ToString())
            .RunAsync(async _ =>
            {
                foreach (var step in steps)
                {
                    await step(cancellationToken);
                }
            });
    }

    internal Task<IWebBackend> GetOrCreateBackendAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_backendGate)
        {
            if (_backendTask is not null)
            {
                return _backendTask;
            }

            var task = CreateBackendAsync(cancellationToken);
            _backendTask = task;

            // A failed creation must not be cached: the next call starts from a clean slate. The
            // continuation runs synchronously for a failure that happened before the first await, and
            // the task is already published by then, so the clear always wins.
            _ = task.ContinueWith(
                _ =>
                {
                    lock (_backendGate)
                    {
                        if (ReferenceEquals(_backendTask, task))
                        {
                            _backendTask = null;
                        }
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
            return task;
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
            // The test's token: teardown I/O (Playwright's trace read, Selenium's final driver call)
            // observes the same cancellation the test lifecycle does.
            await backend.CompleteAsync(_context.CancellationToken);
            operation.Succeed();
        }
        catch (Exception exception)
        {
            operation.Fail(exception);
            throw;
        }
    }

    ValueTask IProtoClientCompletion.CompleteAsync() => CompleteAsync();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0) return;
        await CompleteAsync();
        if (_backendTask is { } backendTask)
            await (await backendTask).DisposeAsync();
    }
}
