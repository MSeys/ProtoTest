namespace ProtoTest.Web.Internal;

using ProtoTest.Core;

/// <summary>
/// Captures the failure artifacts both backends produce — screenshot, DOM and location — with one
/// naming rule and one diagnostic path. Each capture is isolated so a failing artifact never replaces
/// the original web error.
/// </summary>
internal static class WebFailureArtifacts
{
    internal static async ValueTask<IReadOnlyList<ProtoTestAttachment>> CaptureAsync(
        ProtoExecutionContext context,
        string traceSource,
        string backendName,
        string sessionName,
        WebFailureContext failure,
        int sequence,
        Func<string, ValueTask<ProtoTestAttachment?>> screenshot,
        Func<string, ValueTask<ProtoTestAttachment?>> dom,
        Func<string, ValueTask<ProtoTestAttachment?>> location)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);
        // The per-failure sequence keeps a repeated failure of the same element's artifacts distinct.
        var prefix =
            $"{WebNames.SafeName(sessionName)}-{WebNames.SafeName(failure.Element?.Name ?? failure.Operation)}-{sequence}";
        var attachments = new List<ProtoTestAttachment>(3);
        await CaptureAsync(context, traceSource, backendName, attachments, prefix, "screenshot", screenshot);
        await CaptureAsync(context, traceSource, backendName, attachments, prefix, "dom", dom);
        await CaptureAsync(context, traceSource, backendName, attachments, prefix, "location", location);
        return attachments;
    }

    private static async ValueTask CaptureAsync(
        ProtoExecutionContext context,
        string traceSource,
        string backendName,
        List<ProtoTestAttachment> attachments,
        string prefix,
        string artifact,
        Func<string, ValueTask<ProtoTestAttachment?>> capture)
    {
        try
        {
            if (await capture(prefix) is { } attachment)
            {
                attachments.Add(attachment);
            }
        }
        catch (Exception exception)
        {
            context.Trace.WriteEvent(
                "web.diagnostics.artifact_failed",
                $"{backendName} diagnostic failed · {artifact}",
                traceSource,
                outcome: ProtoTraceOutcome.Failed,
                attributes: new Dictionary<string, string?> { ["web.artifact"] = artifact },
                exception: exception);
        }
    }
}
