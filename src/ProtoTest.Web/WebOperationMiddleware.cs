namespace ProtoTest.Web;

using ProtoTest.Core;

public enum WebOperationKind
{
    Navigate,
    Click,
    Fill,
    Check,
    SelectOption,
    Press,
    Count,
    ReadText,
    ReadValue,
    IsVisible,
    IsEnabled,
    IsChecked,
    Assert
}

public enum WebKey
{
    Enter,
    Tab,
    Escape,
    Space,
    Backspace,
    Delete,
    ArrowUp,
    ArrowDown,
    ArrowLeft,
    ArrowRight,
    Home,
    End,
    PageUp,
    PageDown
}

public sealed class WebOperationContext
{
    internal WebOperationContext(
        ProtoExecutionContext execution,
        WebOperationKind kind,
        string name,
        string backendName,
        string sessionName,
        string correlationId,
        WebElementReference? element,
        IWebBackend backend)
    {
        Execution = execution;
        Kind = kind;
        Name = name;
        BackendName = backendName;
        SessionName = sessionName;
        CorrelationId = correlationId;
        Element = element;
        Backend = backend;
    }

    public ProtoExecutionContext Execution { get; }
    public WebOperationKind Kind { get; }
    public string Name { get; }
    public string BackendName { get; }
    public string SessionName { get; }
    public string CorrelationId { get; }
    public WebElementReference? Element { get; }
    public object? Result { get; internal set; }
    internal IWebBackend Backend { get; }
}

public delegate ValueTask WebOperationDelegate(
    WebOperationContext context,
    CancellationToken cancellationToken);

/// <summary>Wraps semantic web operations without exposing backend-specific pipeline stages.</summary>
public interface IWebOperationMiddleware
{
    ValueTask InvokeAsync(
        WebOperationContext context,
        WebOperationDelegate next,
        CancellationToken cancellationToken = default);
}

public enum WebWaitTiming
{
    Before,
    After
}

public sealed record WebWaitObservation(bool Satisfied, string? LastObserved = null)
{
    public static WebWaitObservation Ready(string? observation = null) => new(true, observation);
    public static WebWaitObservation Pending(string? observation = null) => new(false, observation);
}

public interface IWebWaitCondition
{
    string Name { get; }
    ValueTask<WebWaitObservation> ObserveAsync(
        WebWaitContext context,
        CancellationToken cancellationToken = default);
}

public sealed class WebWaitContext
{
    private readonly IWebBackend _backend;

    internal WebWaitContext(WebOperationContext operation, IWebBackend backend)
    {
        Operation = operation;
        _backend = backend;
    }

    public WebOperationContext Operation { get; }
    public ValueTask<int> CountAsync(WebElementReference elements, CancellationToken cancellationToken = default)
        => _backend.CountAsync(elements, cancellationToken);
    public ValueTask<bool> IsVisibleAsync(WebElementReference element, CancellationToken cancellationToken = default)
        => _backend.IsVisibleAsync(element, cancellationToken);
    public ValueTask<bool> EvaluateBooleanAsync(string script, CancellationToken cancellationToken = default)
        => _backend.EvaluateBooleanAsync(script, cancellationToken);
}

/// <summary>Built-in application wait that is a no-op when jQuery is absent.</summary>
public sealed class JQueryIdleWait : IWebWaitCondition
{
    public string Name => "jQuery idle";

    public async ValueTask<WebWaitObservation> ObserveAsync(
        WebWaitContext context,
        CancellationToken cancellationToken = default)
    {
        var ready = await context.EvaluateBooleanAsync(
            "typeof window.jQuery === 'undefined' || window.jQuery.active === 0",
            cancellationToken);
        return ready
            ? WebWaitObservation.Ready("jQuery is absent or has no active requests")
            : WebWaitObservation.Pending("jQuery.active is greater than zero");
    }
}
