namespace ProtoTest.Web;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

/// <summary>Test-scoped entry point for pages, operations, and explicit native backend access.</summary>
public sealed class WebSession : IAsyncDisposable
{
    private const string TraceSource = "ProtoTest.Web";
    private readonly ProtoExecutionContext _context;
    private readonly IWebBackendFactory _factory;
    private readonly ProtoLock _backendGate = new();
    private Task<IWebBackend>? _backendTask;
    private int _completeStarted;
    private int _disposeStarted;

    internal WebSession(ProtoExecutionContext context, IWebBackendFactory factory, string name)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public string Name { get; }
    public string BackendName => _backendTask is { IsCompletedSuccessfully: true }
        ? _backendTask.Result.Name
        : _factory.Name;

    public TPage Page<TPage>() where TPage : WebPage, new()
    {
        var page = new TPage();
        page.Initialize(this, new ComponentScope([], typeof(TPage).Name));
        return page;
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
        => ExecuteVoidAsync(
            "web.navigate",
            $"WEB · Navigate · {address}",
            WebOperationKind.Navigate,
            null,
            new Dictionary<string, string?> { ["web.address"] = address.ToString() },
            (backend, ct) => backend.NavigateAsync(address, ct),
            cancellationToken);

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
                var started = System.Diagnostics.Stopwatch.StartNew();
                string? lastObserved = null;
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var observation = await inspect(backend, ct);
                        lastObserved = observation.Observation;
                        if (observation.Matches) return;
                    }
                    catch (WebElementResolutionException exception)
                    {
                        lastObserved = exception.Message;
                    }
                    catch (WebActionabilityException exception)
                    {
                        lastObserved = exception.Message;
                    }

                    if (started.Elapsed >= assertionTimeout)
                        throw new WebAssertionException(
                            $"Element '{element.ComponentPath}.{element.Name}' should {expectation} within {assertionTimeout}. " +
                            $"Last observed: {lastObserved ?? "no observation"}.");
                    var remaining = assertionTimeout - started.Elapsed;
                    await Task.Delay(
                        remaining < TimeSpan.FromMilliseconds(50) ? remaining : TimeSpan.FromMilliseconds(50),
                        ct);
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
        using var operation = _context.Trace.StartOperation(kind, name, TraceSource, attributes: attributes);
        var backendContext = new WebBackendOperationContext(
            operation.Id, Name, operationKind, OperationName(name), element);
        var webOperation = new WebOperationContext(
            _context, operationKind, OperationName(name), backend.Name, Name, operation.Id, element, backend);
        try
        {
            WebOperationDelegate pipeline = async (context, ct) =>
            {
                using var backendOperation = _context.Trace.StartOperation(
                    "web.backend.execute",
                    $"{backend.Name} · {operationKind}",
                    TraceSource,
                    attributes: new Dictionary<string, string?>
                    {
                        ["web.backend"] = backend.Name,
                        ["web.session"] = Name,
                        ["web.correlation_id"] = operation.Id
                    },
                    parentId: operation.Id);
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
        try
        {
            var attachments = await backend.CaptureFailureAsync(failure);
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
        using var operation = _context.Trace.StartOperation(
            "web.flow",
            $"WEB flow · {name}",
            TraceSource,
            attributes: new Dictionary<string, string?>
            {
                ["web.session"] = Name,
                ["web.backend"] = BackendName,
                ["web.flow.step_count"] = steps.Count.ToString()
            });
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
        using var operation = _context.Trace.StartOperation(
            "web.session.initialize",
            $"Initialize web session · {Name}",
            TraceSource,
            attributes: new Dictionary<string, string?>
            {
                ["web.session"] = Name,
                ["web.backend"] = _factory.Name
            });
        try
        {
            var backend = await _factory.CreateAsync(_context, cancellationToken);
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
        using var operation = _context.Trace.StartOperation(
            "web.session.complete",
            $"Complete web session · {Name}",
            TraceSource,
            ProtoTracePhase.Teardown,
            new Dictionary<string, string?>
            {
                ["web.backend"] = backend.Name,
                ["web.session"] = Name
            });
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
