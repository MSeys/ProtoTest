namespace ProtoTest.Web.Internal;

using ProtoTest.Core;

/// <summary>
/// Captures the failure artifacts both backends produce — screenshot, DOM and location — with one
/// naming rule, one set of descriptions and one location format. Each capture is isolated so a failing
/// artifact never replaces the original web error.
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
        Func<ValueTask<byte[]?>> screenshot,
        Func<ValueTask<string?>> dom,
        Func<ValueTask<(string? Url, string? Title)>> location)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentException.ThrowIfNullOrWhiteSpace(backendName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);
        // The per-failure sequence keeps a repeated failure of the same element's artifacts distinct.
        var prefix =
            $"{WebNames.SafeName(sessionName)}-{WebNames.SafeName(failure.Element?.Name ?? failure.Operation)}-{sequence}";
        var attachments = new List<ProtoTestAttachment>(3);
        await CaptureAsync(context, traceSource, backendName, attachments, "screenshot", async () =>
        {
            var bytes = await screenshot();
            return bytes is null
                ? null
                : ProtoTestAttachment.FromBytes(
                    $"web-{prefix}-failure.png",
                    bytes,
                    "image/png",
                    $"{backendName} page at web operation failure.");
        });
        await CaptureAsync(context, traceSource, backendName, attachments, "dom", async () =>
        {
            var content = await dom();
            return content is null
                ? null
                : ProtoTestAttachment.FromText(
                    $"web-{prefix}-page.html",
                    content,
                    "text/html",
                    "DOM snapshot at web operation failure.");
        });
        await CaptureAsync(context, traceSource, backendName, attachments, "location", async () =>
        {
            var (url, title) = await location();
            return ProtoTestAttachment.FromText(
                $"web-{prefix}-location.txt",
                $"URL: {url}{Environment.NewLine}Title: {title}",
                "text/plain",
                "Browser location at web operation failure.");
        });
        return attachments;
    }

    private static async ValueTask CaptureAsync(
        ProtoExecutionContext context,
        string traceSource,
        string backendName,
        List<ProtoTestAttachment> attachments,
        string artifact,
        Func<ValueTask<ProtoTestAttachment?>> capture)
    {
        try
        {
            if (await capture() is { } attachment)
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
