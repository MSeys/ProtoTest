namespace ProtoTest.Web;

using ProtoTest.Core;

/// <summary>The immutable semantic reference passed to a concrete web backend.</summary>
public sealed record WebElementReference(
    IReadOnlyList<WebLocator> ComponentRoots,
    string ComponentPath,
    string Name,
    WebLocator Locator);

public sealed record WebBackendOperationContext(
    string CorrelationId,
    string SessionName,
    WebOperationKind Kind,
    string Name,
    WebElementReference? Element);

/// <summary>Executes ProtoTest web concepts using a concrete browser technology.</summary>
public interface IWebBackend : IAsyncDisposable
{
    string Name { get; }
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

/// <summary>Optional backend capability: evaluates a boolean JavaScript expression in the page.</summary>
public interface IWebBackendJavaScript
{
    ValueTask<bool> EvaluateBooleanAsync(string script, CancellationToken cancellationToken = default);
}

/// <summary>Optional backend capability: captures best-effort diagnostics for a failed operation.</summary>
public interface IWebBackendDiagnostics
{
    ValueTask<IReadOnlyList<ProtoTestAttachment>> CaptureFailureAsync(
        WebFailureContext failure,
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
