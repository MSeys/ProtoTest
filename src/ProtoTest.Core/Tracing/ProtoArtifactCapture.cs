namespace ProtoTest.Core;

/// <summary>
/// Reads one declared attachment into an artifact source for the archive. A read failure is recorded on
/// the artifact rather than thrown, so a trace still opens and names what could not be captured.
/// </summary>
internal static class ProtoArtifactCapture
{
    public static async ValueTask<ProtoTraceArtifactSource> CaptureAsync(
        ProtoTraceArtifact artifact,
        ProtoTestAttachment attachment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(attachment);

        ReadOnlyMemory<byte> content = ReadOnlyMemory<byte>.Empty;
        try
        {
            content = await attachment.ReadAllBytesAsync(cancellationToken);
            artifact = artifact with { SizeBytes = content.Length };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            artifact = artifact with { Error = exception.Message };
        }

        return new ProtoTraceArtifactSource(artifact, content);
    }
}
