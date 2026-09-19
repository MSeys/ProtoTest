namespace ProtoTest.Web;

using ProtoTest.Core;

/// <summary>The immutable semantic reference passed to a concrete web backend.</summary>
public sealed record WebElementReference(
    IReadOnlyList<WebLocator> ComponentRoots,
    string ComponentPath,
    string Name,
    WebLocator Locator);

/// <summary>
/// One semantic operation as the backend sees it. <c>ParentCorrelationId</c> is set only when the
/// operation was started inside another operation's explicit nesting scope (a <c>WaitUntilAsync</c>
/// condition), which lets a backend group native diagnostics by operation lineage instead of guessing
/// from flow-local state.
/// </summary>
public sealed record WebBackendOperationContext(
    string CorrelationId,
    string SessionName,
    WebOperationKind Kind,
    string Name,
    WebElementReference? Element,
    string? ParentCorrelationId = null);

/// <summary>Executes ProtoTest web concepts using a concrete browser technology.</summary>
public interface IWebBackend : IAsyncDisposable
{
    string Name { get; }

    /// <summary>
    /// The address the browser is on now, or <see langword="null"/> when the backend cannot report one.
    /// Page coverage reads it after a navigation (so a redirect is attributed to its final page) and
    /// after a passing assertion.
    /// </summary>
    string? CurrentAddress => null;

    ValueTask NavigateAsync(Uri address, CancellationToken cancellationToken = default);
    ValueTask ClickAsync(WebElementReference element, CancellationToken cancellationToken = default);
    ValueTask FillAsync(WebElementReference element, string value, CancellationToken cancellationToken = default);
    ValueTask CheckAsync(WebElementReference element, bool isChecked, CancellationToken cancellationToken = default);
    ValueTask SelectOptionAsync(WebElementReference element, string value, CancellationToken cancellationToken = default);
    ValueTask PressAsync(WebElementReference element, WebKey key, CancellationToken cancellationToken = default);
    ValueTask<int> CountAsync(WebElementReference elements, CancellationToken cancellationToken = default);
    ValueTask<string> ReadTextAsync(WebElementReference element, CancellationToken cancellationToken = default);
    ValueTask<string?> ReadValueAsync(WebElementReference element, CancellationToken cancellationToken = default);
    ValueTask<bool> IsVisibleAsync(WebElementReference element, CancellationToken cancellationToken = default);
    ValueTask<bool> IsEnabledAsync(WebElementReference element, CancellationToken cancellationToken = default);
    ValueTask<bool> IsCheckedAsync(WebElementReference element, CancellationToken cancellationToken = default);

    /// <summary>Lets a backend correlate one semantic ProtoTrace operation with native diagnostics.</summary>
    ValueTask BeginOperationAsync(
        WebBackendOperationContext operation,
        CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <summary>Closes native diagnostic correlation started for a semantic operation.</summary>
    ValueTask EndOperationAsync(
        WebBackendOperationContext operation,
        ProtoTraceOutcome outcome,
        Exception? exception = null,
        CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <summary>Finalizes artifacts while Core's normal test teardown can still publish them.</summary>
    ValueTask CompleteAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}

/// <summary>Optional backend capability: evaluates JavaScript in the page.</summary>
public interface IWebBackendJavaScript
{
    ValueTask<bool> EvaluateBooleanAsync(string script, CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates a JavaScript expression that answers with a JSON string (or <see langword="null"/>)
    /// and returns that string. The default answers <see langword="null"/>, so a backend that only
    /// speaks booleans stays usable; callers treat the result as best-effort.
    /// </summary>
    ValueTask<string?> EvaluateJsonAsync(string script, CancellationToken cancellationToken = default)
        => ValueTask.FromResult<string?>(null);
}

/// <summary>Optional backend capability: captures best-effort diagnostics for a failed operation.</summary>
public interface IWebBackendDiagnostics
{
    ValueTask<IReadOnlyList<ProtoTestAttachment>> CaptureFailureAsync(
        WebFailureContext failure,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Optional backend capability: captures a file the browser downloads. Playwright implements it with
/// its native download waiter. Selenium implements it by throwing <see cref="WebBackendCapabilityException"/>
/// because the WebDriver protocol has no download API, so a session using Selenium fails before the
/// trigger runs instead of quietly capturing nothing.
/// </summary>
public interface IWebBackendDownloads
{
    /// <summary>
    /// Runs <paramref name="trigger"/> (the action that starts the download) and returns the file it
    /// produced, waiting up to <paramref name="timeout"/> when one is given.
    /// </summary>
    ValueTask<WebDownload> DownloadAsync(
        Func<CancellationToken, Task> trigger,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}

public interface IWebBackendFactory
{
    string Name { get; }

    /// <summary>Creates the backend for one named, test-scoped session.</summary>
    ValueTask<IWebBackend> CreateAsync(
        ProtoExecutionContext context,
        string sessionName,
        CancellationToken cancellationToken = default);
}

public sealed record WebFailureContext(
    string Operation,
    WebElementReference? Element,
    Exception Exception);

public sealed class WebBackendCapabilityException(string message) : NotSupportedException(message);

public sealed class WebElementResolutionException(string message) : InvalidOperationException(message);

public sealed class WebActionabilityException(string message) : TimeoutException(message);

public sealed class WebWaitTimeoutException(string message) : TimeoutException(message);

public sealed class WebAssertionException(string message) : ProtoAssertionException(message);
