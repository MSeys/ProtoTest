namespace ProtoTest.Web;

/// <summary>A file the browser downloaded while a web operation ran.</summary>
/// <param name="FileName">The suggested file name, or the name the test asked for.</param>
/// <param name="MediaType">A best-effort media type guessed from the file's extension.</param>
/// <param name="Content">The downloaded bytes.</param>
public sealed record WebDownload(
    string FileName,
    string MediaType,
    ReadOnlyMemory<byte> Content)
{
    /// <summary>The size of the downloaded file in bytes.</summary>
    public long Size => Content.Length;
}
