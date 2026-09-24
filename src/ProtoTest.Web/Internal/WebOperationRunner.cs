namespace ProtoTest.Web.Internal;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

/// <summary>
/// Runs one semantic web operation: it resolves the backend, opens the ProtoTrace operation, walks the
/// middleware chain, drives the backend's own operation correlation, captures failure diagnostics and
/// completes the operation with the right outcome. <see cref="WebSession"/> owns identity, pages and
/// navigation; this owns the operation pipeline they all funnel through.
/// </summary>
internal sealed class WebOperationRunner(
    ProtoExecutionContext context,
    string sessionName,
    string traceSource,
    Func<CancellationToken, ValueTask<IWebBackend>> getBackendAsync)
{
    private readonly AsyncLocal<string?> _activeNestingScope = new();

    public async ValueTask ExecuteVoidAsync(
        string kind,
        string name,
        WebOperationKind operationKind,
        WebElementReference? element,
        Dictionary<string, string?> attributes,
        Func<IWebBackend, CancellationToken, ValueTask> execute,
        CancellationToken cancellationToken,
        bool opensNestingScope = false)
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
            cancellationToken,
            opensNestingScope);

    /// <summary>Runs one semantic operation with the trace vocabulary its descriptor owns.</summary>
    public ValueTask ExecuteVoidAsync(
        WebOperation operation,
        WebElementReference element,
        Dictionary<string, string?> attributes,
        Func<IWebBackend, CancellationToken, ValueTask> execute,
        CancellationToken cancellationToken,
        string? detail = null)
        => ExecuteVoidAsync(
            operation.TraceKind,
            operation.TraceName(element, detail),
            operation.Kind,
            element,
            attributes,
            execute,
            cancellationToken);

    /// <summary>Reads one semantic operation's result with the trace vocabulary its descriptor owns.</summary>
    public ValueTask<TResult> ExecuteAsync<TResult>(
        WebOperation operation,
        WebElementReference element,
        Dictionary<string, string?> attributes,
        Func<IWebBackend, CancellationToken, ValueTask<TResult>> execute,
        CancellationToken cancellationToken,
        string? detail = null)
        => ExecuteAsync(
            operation.TraceKind,
            operation.TraceName(element, detail),
            operation.Kind,
            element,
            attributes,
            execute,
            cancellationToken);

    public async ValueTask<TResult> ExecuteAsync<TResult>(
        string kind,
        string name,
        WebOperationKind operationKind,
        WebElementReference? element,
        Dictionary<string, string?> attributes,
        Func<IWebBackend, CancellationToken, ValueTask<TResult>> execute,
        CancellationToken cancellationToken,
        bool opensNestingScope = false,
        Action<TResult, ProtoTraceOperation>? afterCapture = null)
    {
        var backend = await getBackendAsync(cancellationToken);
        attributes["web.backend"] = backend.Name;
        attributes["web.session"] = sessionName;
        using var operation = context.Trace
            .Operation(kind, name, traceSource)
            .With(attributes)
            .Begin();
        var backendContext = new WebBackendOperationContext(
            operation.Id, sessionName, operationKind, OperationName(name), element, _activeNestingScope.Value);
        // An explicit nesting scope is the only way an operation becomes nested: a second top-level
        // operation started while another one is still in flight is never misclassified, and a
        // fire-and-forget operation cannot leave a stale correlation behind because the scope is
        // restored when this operation returns.
        using var nesting = opensNestingScope
            ? new NestingScope(this, operation.Id)
            : null;
        var webOperation = new WebOperationContext(
            context, operationKind, OperationName(name), backend.Name, sessionName, operation.Id, element, backend);
        try
        {
            // The tail writes the result into this typed local; middlewares wrap the pipeline but never
            // carry the value, so the runner never casts an object back to the caller's type.
            TResult result = default!;
            WebOperationDelegate pipeline = async (operationContext, ct) =>
            {
                using var backendOperation = context.Trace
                    .Operation("web.backend.execute", $"{backend.Name} · {operationKind}", traceSource)
                    .With("web.backend", backend.Name)
                    .With("web.session", sessionName)
                    .With("web.correlation_id", operation.Id)
                    .Parent(operation.Id)
                    .Begin();
                Exception? failure = null;
                var outcome = ProtoTraceOutcome.Unknown;
                try
                {
                    await backend.BeginOperationAsync(backendContext, ct);
                    result = await execute(backend, ct);
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

            foreach (var middleware in context.Services.GetServices<IWebOperationMiddleware>().Reverse())
            {
                var next = pipeline;
                pipeline = (operationContext, ct) => middleware.InvokeAsync(operationContext, next, ct);
            }

            await pipeline(webOperation, cancellationToken);

            afterCapture?.Invoke(result, operation);
            operation.Succeed();
            return result;
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

        IReadOnlyList<ProtoTestAttachment> attachments;
        try
        {
            attachments = await diagnostics.CaptureFailureAsync(failure);
        }
        catch (Exception captureException)
        {
            context.Trace.WriteEvent(
                "web.diagnostics.failed",
                "Web diagnostics capture failed",
                traceSource,
                outcome: ProtoTraceOutcome.Failed,
                exception: captureException,
                parentId: parentId);
            return;
        }

        // Each artifact registers on its own: one failing attachment (for example a collision) must not
        // drop the rest of the failure evidence.
        foreach (var attachment in attachments)
        {
            try
            {
                context.AddAttachment(attachment);
            }
            catch (Exception attachmentException)
            {
                context.Trace.WriteEvent(
                    "web.diagnostics.artifact_failed",
                    $"Web diagnostic failed · {attachment.Name}",
                    traceSource,
                    outcome: ProtoTraceOutcome.Failed,
                    attributes: new Dictionary<string, string?> { ["web.artifact"] = attachment.Name },
                    exception: attachmentException,
                    parentId: parentId);
            }
        }
    }

    /// <summary>
    /// Sets the correlation id nested operations resolve while the scope is open and restores the
    /// previous value on dispose, so no flow-local state outlives the operation that owns it.
    /// </summary>
    private sealed class NestingScope : IDisposable
    {
        private readonly WebOperationRunner _runner;
        private readonly string? _previous;

        public NestingScope(WebOperationRunner runner, string correlationId)
        {
            _runner = runner;
            _previous = runner._activeNestingScope.Value;
            runner._activeNestingScope.Value = correlationId;
        }

        public void Dispose() => _runner._activeNestingScope.Value = _previous;
    }
}
