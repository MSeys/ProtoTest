namespace ProtoTest.Core;

/// <summary>Names the failure event one integration writes when an attachment cannot be added.</summary>
public sealed record ProtoAttachmentFailure(string Kind, string Source, string Label)
{
    /// <summary>The event name for one attachment, so every protocol's failures read the same way.</summary>
    public string Describe(string attachmentName) => $"{Label} · {attachmentName}";
}

/// <summary>
/// Adds a captured payload as an attachment without ever failing the operation: a failure is traced as
/// a failed event with the operation as its parent, the rule gRPC, messaging and web diagnostics share.
/// </summary>
public static class ProtoAttachmentCapture
{
    public static void TryAdd(
        ProtoExecutionContext context,
        ProtoTraceOperation operation,
        ProtoAttachmentFailure failure,
        string attachmentName,
        Func<string> content,
        string mediaType,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentException.ThrowIfNullOrWhiteSpace(attachmentName);
        ArgumentNullException.ThrowIfNull(content);
        try
        {
            // The content factory runs inside the guard: a message that cannot be formatted or
            // sanitized is an attachment failure, not a call failure.
            context.AddAttachment(attachmentName, content(), mediaType, description);
        }
        catch (Exception exception)
        {
            context.Trace.WriteEvent(
                failure.Kind,
                failure.Describe(attachmentName),
                failure.Source,
                outcome: ProtoTraceOutcome.Failed,
                attributes: new Dictionary<string, string?> { ["attachment.name"] = attachmentName },
                exception: exception,
                parentId: operation.Id);
        }
    }
}
