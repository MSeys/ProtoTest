namespace ProtoTest.Core;

using System.Security.Cryptography;
using System.Text;

/// <summary>
/// The identity a contract collector records for the document it loaded: the configured source and the
/// SHA-256 hash of the loaded content. The report item that carries it has no coverage verdict, so the
/// report arithmetic ignores it; a cross-run comparison can still tell a changed specification from the
/// same one.
/// </summary>
public static class ProtoSpecIdentity
{
    /// <summary>The metadata key of the configured source the specification was loaded from.</summary>
    public const string SourceMetadataKey = "spec.source";

    /// <summary>The metadata key of the SHA-256 hash of the loaded content, lowercase hexadecimal.</summary>
    public const string HashMetadataKey = "spec.hash";

    /// <summary>The identifier of the aggregate report item that carries the identity.</summary>
    public const string ReportIdentifier = "spec";

    /// <summary>The SHA-256 hash of loaded content, lowercase hexadecimal.</summary>
    public static string Hash(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
    }

    /// <summary>
    /// The identity metadata for a loaded specification. A single absolute URL has its credentials and
    /// sensitive query values removed through <see cref="ProtoUriSanitizer"/>; anything else, including
    /// an inline document, is recorded as it is.
    /// </summary>
    public static IReadOnlyDictionary<string, object> Metadata(string source, string content)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(content);
        return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            [SourceMetadataKey] = RecordedSource(source),
            [HashMetadataKey] = Hash(content)
        };
    }

    private static string RecordedSource(string source)
        => Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? ProtoUriSanitizer.Sanitize(source, ProtoUriSanitizer.DefaultSensitiveQueryParameters)
            : source;
}
