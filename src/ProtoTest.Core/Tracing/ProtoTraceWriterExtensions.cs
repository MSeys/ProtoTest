namespace ProtoTest.Core;

/// <summary>Reduces the start/await/succeed-or-fail boilerplate repeated across ProtoTest integrations.</summary>
public static class ProtoTraceWriterExtensions
{
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
}
