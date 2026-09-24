namespace ProtoTest.Core;

/// <summary>
/// Reads one declared attachment into an artifact source for the archive. A read failure - or an
/// attachment over the configured size limit - is recorded on the artifact rather than thrown, so a
/// trace still opens and names what could not be captured.
/// </summary>
internal static class ProtoArtifactCapture
{
    public static async ValueTask<ProtoTraceArtifactSource> CaptureAsync(
        ProtoTraceArtifact artifact,
        ProtoTestAttachment attachment,
        long maxBytes,
        bool embedArtifacts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(attachment);

        if (!embedArtifacts)
        {
            // Declared, not embedded: the name and size stay visible without reading or retaining bytes.
            long? size = attachment.IsFile && TryGetFileLength(attachment.FilePath!, out var declaredLength)
                ? declaredLength
                : null;
            return new ProtoTraceArtifactSource(
                artifact with { SizeBytes = size, Error = "Artifact embedding is disabled." },
                ReadOnlyMemory<byte>.Empty);
        }

        if (attachment.IsFile && TryGetFileLength(attachment.FilePath!, out var fileLength) && fileLength > maxBytes)
        {
            return new ProtoTraceArtifactSource(
                artifact with { SizeBytes = fileLength, Error = LimitMessage(fileLength, maxBytes) },
                ReadOnlyMemory<byte>.Empty);
        }

        ReadOnlyMemory<byte> content = ReadOnlyMemory<byte>.Empty;
        try
        {
            content = await attachment.ReadAllBytesAsync(cancellationToken);
            if (content.Length > maxBytes)
            {
                return new ProtoTraceArtifactSource(
                    artifact with { SizeBytes = content.Length, Error = LimitMessage(content.Length, maxBytes) },
                    ReadOnlyMemory<byte>.Empty);
            }

            artifact = artifact with { SizeBytes = content.Length };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            artifact = artifact with { Error = exception.Message };
        }

        return new ProtoTraceArtifactSource(artifact, content);
    }

    private static string LimitMessage(long size, long maxBytes)
        => $"The attachment is {size} bytes, which exceeds the configured artifact limit of {maxBytes} bytes.";

    private static bool TryGetFileLength(string path, out long length)
    {
        try
        {
            length = new FileInfo(path).Length;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            length = 0;
            return false;
        }
    }
}
