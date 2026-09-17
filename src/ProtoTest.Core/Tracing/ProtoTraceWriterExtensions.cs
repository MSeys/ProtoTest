namespace ProtoTest.Core;

/// <summary>Reduces the start/await/succeed-or-fail boilerplate repeated across ProtoTest integrations.</summary>
public static class ProtoTraceWriterExtensions
{
    /// <summary>
    /// Starts a fluent <see cref="ProtoTraceScope"/> for a traced operation. Chain <c>During</c>,
    /// <c>Parent</c> and <c>With</c>, then finish with <c>RunAsync</c> or <c>Begin</c>.
    /// </summary>
    public static ProtoTraceScope Operation(
        this IProtoTraceWriter trace,
        string kind,
        string name,
        string source)
    {
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        return new ProtoTraceScope(trace, kind, name, source, ProtoTracePhase.Execution, null, null);
    }

    /// <summary>Wraps <paramref name="action"/> in a traced operation, marking it succeeded, cancelled, or failed.</summary>
    public static async ValueTask ExecuteAsync(
        this IProtoTraceWriter trace,
        string kind,
        string name,
        string source,
        Func<ValueTask> action,
        ProtoTracePhase phase = ProtoTracePhase.Execution,
        IReadOnlyDictionary<string, string?>? attributes = null,
        string? parentId = null)
    {
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentNullException.ThrowIfNull(action);
        using var operation = trace.StartOperation(kind, name, source, phase, attributes, parentId);
        try
        {
            await action();
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

    /// <summary>Wraps <paramref name="action"/> in a traced operation, returning its result on success.</summary>
    public static async ValueTask<TResult> ExecuteAsync<TResult>(
        this IProtoTraceWriter trace,
        string kind,
        string name,
        string source,
        Func<ValueTask<TResult>> action,
        ProtoTracePhase phase = ProtoTracePhase.Execution,
        IReadOnlyDictionary<string, string?>? attributes = null,
        string? parentId = null)
    {
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentNullException.ThrowIfNull(action);
        using var operation = trace.StartOperation(kind, name, source, phase, attributes, parentId);
        try
        {
            var result = await action();
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
            operation.Fail(exception);
            throw;
        }
    }

    /// <summary>
    /// Wraps <paramref name="action"/> in a traced operation and passes it the live handle so it can
    /// attach attributes mid-flight, marking the operation succeeded, cancelled, or failed.
    /// The handle must not outlive <paramref name="action"/>.
    /// </summary>
    public static async ValueTask ExecuteAsync(
        this IProtoTraceWriter trace,
        string kind,
        string name,
        string source,
        Func<ProtoTraceOperation, ValueTask> action,
        ProtoTracePhase phase = ProtoTracePhase.Execution,
        IReadOnlyDictionary<string, string?>? attributes = null,
        string? parentId = null)
    {
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentNullException.ThrowIfNull(action);
        using var operation = trace.StartOperation(kind, name, source, phase, attributes, parentId);
        try
        {
            await action(operation);
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

    /// <summary>
    /// Wraps <paramref name="action"/> in a traced operation, passing it the live handle and returning
    /// its result on success. The handle must not outlive <paramref name="action"/>.
    /// </summary>
    public static async ValueTask<TResult> ExecuteAsync<TResult>(
        this IProtoTraceWriter trace,
        string kind,
        string name,
        string source,
        Func<ProtoTraceOperation, ValueTask<TResult>> action,
        ProtoTracePhase phase = ProtoTracePhase.Execution,
        IReadOnlyDictionary<string, string?>? attributes = null,
        string? parentId = null)
    {
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentNullException.ThrowIfNull(action);
        using var operation = trace.StartOperation(kind, name, source, phase, attributes, parentId);
        try
        {
            var result = await action(operation);
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
            operation.Fail(exception);
            throw;
        }
    }
}
