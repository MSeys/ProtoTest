namespace ProtoTest.Web;

/// <summary>Guesses a media type from a downloaded file's extension. It is a guess, never a promise.</summary>
public static class WebMediaTypes
{
    /// <summary>The media type a file with no known extension gets.</summary>
    public const string Default = "application/octet-stream";

    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".csv"] = "text/csv",
        [".txt"] = "text/plain",
        [".log"] = "text/plain",
        [".json"] = "application/json",
        [".xml"] = "application/xml",
        [".html"] = "text/html",
        [".htm"] = "text/html",
        [".pdf"] = "application/pdf",
        [".zip"] = "application/zip",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".svg"] = "image/svg+xml",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation"
    };

    /// <summary>Returns the media type for <paramref name="fileName"/>'s extension, or <see cref="Default"/>.</summary>
    public static string Guess(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        return ByExtension.TryGetValue(Path.GetExtension(fileName), out var mediaType)
            ? mediaType
            : Default;
    }
}
