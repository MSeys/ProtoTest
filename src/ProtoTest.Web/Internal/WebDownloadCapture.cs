namespace ProtoTest.Web.Internal;

using System.Globalization;
using ProtoTest.Core;

/// <summary>
/// Captures a browser download: it runs the trigger through the operation pipeline, records the file on
/// the operation and registers it as a test attachment under a per-session sequence.
/// </summary>
internal sealed class WebDownloadCapture(
    ProtoExecutionContext context,
    string sessionName,
    string traceSource,
    WebOperationRunner operations)
{
    private int _downloadSequence;

    public ValueTask<WebDownload> DownloadAsync(
        Func<CancellationToken, Task> trigger,
        string? name,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        if (timeout is { } wait && wait <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var requestedName = string.IsNullOrWhiteSpace(name) ? null : name;
        return operations.ExecuteAsync(
            "web.download",
            requestedName is null ? "WEB · Download" : $"WEB · Download · {requestedName}",
            WebOperationKind.Download,
            element: null,
            new Dictionary<string, string?>
            {
                ["web.download.requested_name"] = requestedName,
                ["web.download.timeout"] = timeout?.ToString()
            },
            async (backend, ct) =>
            {
                if (backend is not IWebBackendDownloads downloads)
                {
                    throw new WebBackendCapabilityException(
                        $"Web backend '{backend.Name}' cannot capture browser downloads. Use a backend " +
                        $"that provides the {nameof(IWebBackendDownloads)} capability (Playwright does), " +
                        "or fetch the file over HTTP instead.");
                }

                var captured = await downloads.DownloadAsync(trigger, timeout, ct);
                if (requestedName is null)
                {
                    return captured;
                }

                // An explicit name wins for the record and the attachment. A known extension refines
                // the media type guess; an unknown or missing one keeps the suggested file's type.
                var mediaType = WebMediaTypes.Guess(requestedName);
                return captured with
                {
                    FileName = requestedName,
                    MediaType = mediaType == WebMediaTypes.Default ? captured.MediaType : mediaType
                };
            },
            cancellationToken,
            opensNestingScope: true,
            afterCapture: RegisterDownload);
    }

    /// <summary>
    /// Records the captured file on the download operation and registers it as an attachment. The
    /// artifact registers on its own: a failed attachment is traced and never replaces the download.
    /// </summary>
    private void RegisterDownload(WebDownload download, ProtoTraceOperation operation)
    {
        var attributes = new Dictionary<string, string?>
        {
            ["web.download.name"] = download.FileName,
            ["web.download.media_type"] = download.MediaType,
            ["web.download.size"] = download.Size.ToString(CultureInfo.InvariantCulture)
        };
        foreach (var (key, value) in attributes)
        {
            operation.SetAttribute(key, value);
        }

        var attachmentName = DownloadAttachmentName(download.FileName);
        try
        {
            context.AddAttachment(ProtoTestAttachment.FromBytes(
                attachmentName,
                download.Content,
                download.MediaType,
                $"Downloaded file captured by Web session '{sessionName}'."));
        }
        catch (Exception exception)
        {
            context.Trace.WriteEvent(
                "web.download.attachment_failed",
                $"Web download attachment failed · {attachmentName}",
                traceSource,
                outcome: ProtoTraceOutcome.Failed,
                attributes: new Dictionary<string, string?> { ["web.artifact"] = attachmentName },
                exception: exception,
                parentId: operation.Id);
        }
    }

    private string DownloadAttachmentName(string fileName)
    {
        var sequence = Interlocked.Increment(ref _downloadSequence);
        var name = Path.GetFileName(fileName);
        var extension = Path.GetExtension(name);
        var stem = WebNames.SafeName(Path.GetFileNameWithoutExtension(name));
        if (stem.Length == 0) stem = "download";
        var safeExtension = extension.Length > 1 ? WebNames.SafeName(extension[1..]) : string.Empty;
        var suffix = safeExtension.Length > 0 ? $".{safeExtension}" : string.Empty;
        return $"web-{WebNames.SafeName(sessionName)}-download-{sequence}-{stem}{suffix}";
    }
}
